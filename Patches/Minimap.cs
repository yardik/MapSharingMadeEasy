using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace MapSharingMadeEasy.Patches
{
    [HarmonyPatch(typeof(Minimap))]
    public class HookExplore
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(Minimap), "Explore", new Type[] {typeof(int), typeof(int)})]
        public static bool call_Explore(object instance, int x, int y) => throw new NotImplementedException();
    }

    [HarmonyPatch(typeof(Minimap), "UpdateExplore")]
    static class UpdateExplore_Patch
    {
        private static Stopwatch _stopwatch;
        private static bool explored = false;

        static void Postfix(Player player, bool[] ___m_explored, Minimap __instance, Texture2D ___m_fogTexture,
            List<Minimap.PinData> ___m_pins)
        {
            if (MapData.instance.SyncWith != null)
            {
                Debug.Log($"Syncing Map with Map Table");
                MapData.MergeWithSharedMap(player, __instance, ___m_fogTexture, MapData.instance.SyncWith, ___m_pins,
                    ___m_explored);
                MapData.instance.ClearPendingSync();
                return;
            }

            if (MapData.instance.SyncData != null && MapData.instance.MapSender != "")
            {
                PendingDataToMergeEvent(player);
            } else if (MapData.instance.MapSender != "")
            {
                Debug.Log("Map data had no sender. Rejecting.");
                MapData.instance.ClearPendingSync(); 
            }

            //Decline map data
            if (Input.GetKeyDown(Settings.MapSettings.RejectMapKey.Value))
            {
                RejectData(player);
                return;
            }

            //Accept map data
            if (Input.GetKeyDown(Settings.MapSettings.AcceptMapKey.Value))
            {
                AcceptMap(player, ___m_explored, __instance, ___m_fogTexture, ___m_pins);
                return;
            }

            //If the minimap is open and you hit F9 or F11 - send map data or pins or both via chat
            if (!Minimap.IsOpen()) return;
            var sendPins = Input.GetKeyDown(Settings.MapSettings.SyncPinKey.Value);
            var sendMap = Input.GetKeyDown(Settings.MapSettings.SyncMapKey.Value);

            if (!sendMap && !sendPins) return;

            SendData(sendMap, sendPins, player, ___m_explored, ___m_pins);
        }

        private static void SendData(bool sendingMap, bool sendingPins, Player player, bool[] exploredData,
            List<Minimap.PinData> pins)
        {
            if (sendingMap && Settings.MapSettings.SendPinShares.Value)
                sendingPins = true;

            Debug.Log("MapSync::Sending data to character nearby.");
            Stopwatch sw = new Stopwatch();
            sw.Start();
            var chars = new List<Character>();
            Player.GetCharactersInRange(player.transform.position, 2f, chars);
            chars.RemoveAll(c => !c.IsPlayer() || ((Player) c).GetPlayerName() == player.GetPlayerName());
            if (chars.Count > 0)
            {
                var toPlr = ((Player) chars[0]);
                var toPlrName = toPlr.GetPlayerName().Replace("[", "").Replace("]", "");
                Debug.Log($"MapSync::Sending data to {toPlr.GetPlayerName()}");

                player.Message(MessageHud.MessageType.Center,
                    $"You let {toPlrName} copy your {GetWhatData(sendingMap, sendingPins)}.");

                var mapDataString = MapData.GetMapDataString(player.GetPlayerName(), toPlrName, sendingMap, sendingPins,
                    exploredData, pins);

                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ChatMessage",
                    (object) player.transform.position, (object) 2, (object) player.GetPlayerName(),
                    (object) mapDataString);
            }

            sw.Stop();
            Debug.Log($"Total millis to send: {sw.Elapsed.TotalMilliseconds}");
        }

        private static void AcceptMap(Player player, bool[] exploredData, Minimap minimap, Texture2D fogTexture,
            List<Minimap.PinData> pins)
        {
            if (MapData.instance.SyncData != "")
            {
                player.Message(MessageHud.MessageType.Center,
                    $"You copy {MapData.instance.MapSender}'s map.");
            }

            MapData.ParseReceivedMapData(MapData.instance.SyncData, out var sentFrom, out var sentTo,
                out var pluginVersion, out var receivedPins, out var receivedMapData);
            
            if (receivedMapData != null)
            {
                Debug.Log($"MapSync::{MapData.instance.MapSender}'s map received and will be merged.");
                var sw = new Stopwatch();
                sw.Start();
                var chunks = MapData.MergeMapData(exploredData, receivedMapData, out var mergedData);
                Debug.Log($"Merged exploredData with receivedMapData - merged {chunks} chunks, resulting in mergedData: {mergedData.Length}");
                MapData.ExploreMap(minimap, fogTexture, mergedData);
                sw.Stop();
                Debug.Log($"Total millis to merge map: {sw.Elapsed.TotalMilliseconds}");
            }
            else
            {
                Debug.Log("Null received exploration data, nothing to merge there.");
            }

            if (receivedPins != null && receivedPins.Count > 0 && Settings.MapSettings.AcceptPinShares.Value)
            {
                var sw = new Stopwatch();
                sw.Start();
                var pinsIn = MapData.CreatePinsFromSavedPinData(receivedPins);
                MapData.MergePinData(pinsIn, pins, minimap);
                sw.Stop();
                Debug.Log($"Total millis to merge pins: {sw.Elapsed.TotalMilliseconds}");
            }

            MapData.instance.ClearPendingSync();
        }

        private static void RejectData(Player player)
        {
            if (MapData.instance.SyncData != "")
            {
                Debug.Log($"MapSync::Declined request from {MapData.instance.MapSender} to merge maps.");
                player.Message(MessageHud.MessageType.Center,
                    $"You decline the request from {MapData.instance.MapSender} to share their map with you.");     
            }
            MapData.instance.ClearPendingSync();
        }

        private static void PendingDataToMergeEvent(Player player)
        {
            //If map data came from this character - ignore it!
            if (player.GetPlayerName().Replace("[", "").Replace("]", "").Equals(MapData.instance.MapSender))
            {
                Debug.Log("This is my own mapdata - ignore it.");
                MapData.instance.ClearPendingSync();
                return;
            }

            //Prompt to accept map data periodically
            if (_stopwatch == null || _stopwatch.Elapsed.Seconds > 2 || !_stopwatch.IsRunning)
            {
                Debug.Log($"MapSync::MapData Found from {MapData.instance.MapSender} - posting message.");
                player.Message(MessageHud.MessageType.Center,
                    $"{MapData.instance.MapSender} wants to share their map. ({Settings.MapSettings.AcceptMapKey.Value.ToString()} to accept, {Settings.MapSettings.RejectMapKey.Value.ToString()} to decline).");
                _stopwatch = new Stopwatch();
                _stopwatch.Start();
            }
        }

        private static string GetWhatData(bool map, bool pins)
        {
            if (map && pins)
            {
                return "map and pins";
            }
            else if (map)
            {
                return "map";
            }
            else if (pins)
            {
                return "pins";
            }

            return "";
        }

        static void GenerateTestData(bool[] m_explored, Minimap _instance, Texture2D m_fogTexture,
            List<Minimap.PinData> m_pins)
        {
            Debug.Log("Generating test data...");
            var ySize = _instance.m_textureSize;

            if (!explored)
            {
                var exploredChunkCounter = 0;
                Debug.Log($"MapSync::Discover whole map");
                if (_instance == null)
                {
                    Debug.Log("_instance is null");
                    return;
                }

                if (m_explored == null)
                {
                    Debug.Log("m_explored is null");
                    return;
                }

                for (var i = 0; i < m_explored.Length; i++)
                {
                    if (HookExplore.call_Explore(_instance, i % ySize, i / ySize))
                    {
                        exploredChunkCounter++;
                    }
                }

                if (exploredChunkCounter > 0)
                {
                    m_fogTexture.Apply();
                }

                explored = true;
            }

            var vals = Enum.GetValues(typeof(Minimap.PinType));
            for (int i = 0; i < 100; i++)
            {
                var randSpotX = Random.Range(0, ySize);
                var randSpotY = Random.Range(0, ySize);
                var randPos = new Vector3(randSpotX, 0f, randSpotY);
                var randPinType = Random.Range(0, 5);

                _instance.AddPin(randPos, (Minimap.PinType) vals.GetValue(randPinType), "randompin" + i, true,
                    Random.Range(0, 1) == 0);
            }
        }

        static void ClearTestData(bool[] m_explored, Minimap _instance, Texture2D m_fogTexture,
            List<Minimap.PinData> m_pins)
        {
            Debug.Log("Clearing test data...");
            var ySize = _instance.m_textureSize;

            Debug.Log($"MapSync::Undiscover whole map");
            if (_instance == null)
            {
                Debug.Log("_instance is null");
                return;
            }

            if (m_explored == null)
            {
                Debug.Log("m_explored is null");
                return;
            }

            _instance.Reset();

            var oldPins = new List<Minimap.PinData>();
            m_pins.ForEach(p => oldPins.Add(p));
            oldPins.ForEach(p => _instance.RemovePin(p.m_pos, 5f));
        }
    }
}