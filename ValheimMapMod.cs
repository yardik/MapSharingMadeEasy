using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Timers;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace ValheimMapMod
{
    [BepInPlugin("yardik.MapSharingMadeEasy", "Map Sharing Made Easy", "1.3.0")]
    public class ValheimMapMod : BaseUnityPlugin
    {
        private static ValheimMapMod context;
        private static ConfigEntry<bool> modEnabled;
        private static string PluginVersion = "";

        [HarmonyPatch(typeof(Minimap))]
        public class HookExplore
        {
            [HarmonyReversePatch]
            [HarmonyPatch(typeof(Minimap), "Explore", new Type[] {typeof(int), typeof(int)})]
            public static bool call_Explore(object instance, int x, int y) => throw new NotImplementedException();
        }

        private void Awake()
        {
            var bpp = (BepInPlugin) GetType().GetCustomAttributes(typeof(BepInPlugin), true)[0];
            PluginVersion = bpp.Version.ToString();
            Debug.Log($"MapSharingMadeEasy Version: {PluginVersion}");
            context = this;
            modEnabled = Config.Bind("General", "Enabled", true, "Enable this mod");
            Settings.Init(Config);

            if (!modEnabled.Value)
                return;

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }

        [HarmonyPatch(typeof(Minimap), "UpdateExplore")]
        static class UpdateExplore_Patch
        {
            private static Stopwatch _stopwatch;
            private static string _mapSender = "";
            private static bool[] _pendingMapDataToMerge;
            private static List<SavedPinData> _pendingPinDataToMerge;
            private static bool explored = false;
            
            static void Postfix(Player player, bool[] ___m_explored, Minimap __instance, Texture2D ___m_fogTexture,
                List<Minimap.PinData> ___m_pins)
            {
                if (_pendingMapDataToMerge != null || _pendingPinDataToMerge != null)
                {
                    PendingDataToMergeEvent(player);
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

            private static void SendData(bool sendingMap, bool sendingPins, Player player, bool[] exploredData, List<Minimap.PinData> pins)
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

                    player.Message(MessageHud.MessageType.Center, $"You let {toPlrName} copy your {GetWhatData(sendingMap, sendingPins)}.");

                    var mapString = "";
                    var pinString = "";
                    
                    if (sendingMap)
                        mapString = BuildMapString(exploredData);

                    if (Settings.MapSettings.SendPinShares.Value || sendingPins)
                        pinString = BuildPinString(pins);

                    var compressedMapString = CompressString(mapString);
                    var compressedPinString = CompressString(pinString);

                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ChatMessage",
                        (object) player.transform.position, (object) 2, (object) player.GetPlayerName(),
                        (object)
                        $"MapData:{mapString.Length}:{compressedMapString.Length}:{player.GetPlayerName()}:{compressedMapString}:{compressedPinString}:{toPlr.GetPlayerName()}:{PluginVersion}");
                }

                sw.Stop();
                Debug.Log($"Total millis to send: {sw.Elapsed.TotalMilliseconds}");
            }
            
            private static void AcceptMap(Player player, bool[] exploredData, Minimap minimap, Texture2D fogTexture, List<Minimap.PinData> pins)
            {
                player.Message(MessageHud.MessageType.Center,
                    $"You copy {_mapSender}'s {GetWhatData(_pendingMapDataToMerge != null,ShouldCopyPins())}.");
                
                if (_pendingMapDataToMerge != null)
                {
                    Debug.Log($"MapSync::{_mapSender}'s map received and will be merged.");
                    var sw = new Stopwatch();
                    sw.Start();
                    MergeMapData(exploredData, minimap, fogTexture);
                    sw.Stop();
                    Debug.Log($"Total millis to merge map: {sw.Elapsed.TotalMilliseconds}");

                    _pendingMapDataToMerge = null;
                }

                if (_pendingPinDataToMerge != null && Settings.MapSettings.AcceptPinShares.Value)
                {
                    var sw = new Stopwatch();
                    sw.Start();
                    MergePinData(_pendingPinDataToMerge, pins, minimap);
                    sw.Stop();
                    Debug.Log($"Total millis to merge pins: {sw.Elapsed.TotalMilliseconds}");
                    _pendingPinDataToMerge = null;
                }

                if (_pendingMapDataToMerge == null && _pendingPinDataToMerge == null)
                {
                    _mapSender = null;
                }
            }

            private static void RejectData(Player player)
            {
                Debug.Log($"MapSync::Declined request from {_mapSender} to merge maps.");
                player.Message(MessageHud.MessageType.Center,
                    $"You decline the request from {_mapSender} to share their {GetWhatData(_pendingMapDataToMerge != null,ShouldCopyPins())} with you.");
                _pendingMapDataToMerge = null;
                _pendingPinDataToMerge = null;
                _mapSender = "";
            }
            
            private static void PendingDataToMergeEvent(Player player)
            {
                //If map data came from this character - ignore it!
                if (player.GetPlayerName().Replace("[", "").Replace("]", "").Equals(_mapSender))
                {
                    _pendingMapDataToMerge = null;
                    _pendingPinDataToMerge = null;
                    _mapSender = "";
                    return;
                }

                //Prompt to accept map data periodically
                if (_stopwatch == null || _stopwatch.Elapsed.Seconds > 2)
                {
                    Debug.Log($"MapSync::MapData Found from {_mapSender} - posting message.");
                    player.Message(MessageHud.MessageType.Center,
                        $"{_mapSender} wants to share their {GetWhatData(_pendingMapDataToMerge != null,ShouldCopyPins())} ({Settings.MapSettings.AcceptMapKey.Value.ToString()} to accept, {Settings.MapSettings.RejectMapKey.Value.ToString()} to decline).");
                    _stopwatch = new Stopwatch();
                    _stopwatch.Start();
                }
            }
            
            private static string GetWhatData(bool map, bool pins)
            {
                if (map && pins)
                {
                    return "map and pins";
                } else if (map)
                {
                    return "map";
                } else if (pins)
                {
                    return "pins";
                }

                return "";
            }
            
            static bool ShouldCopyPins()
            {
                return _pendingPinDataToMerge != null && Settings.MapSettings.AcceptPinShares.Value;
            }

            private static float DistanceXZ(Vector3 v0, Vector3 v1)
            {
                var num1 = v1.x - (double) v0.x;
                var num2 = v1.z - v0.z;
                return Mathf.Sqrt((float) (num1 * num1 + num2 * (double) num2));
            }

            private static bool HaveSimilarPin(List<Minimap.PinData> pinData, SavedPinData sp)
            {
                return pinData.Any(pin =>
                {
                    var dist = DistanceXZ(sp.Pos, pin.m_pos);
                    return dist < Settings.MapSettings.SkipPinRange.Value;
                });
            }

            static string BuildPinString(List<Minimap.PinData> pins)
            {
                var sb = new StringBuilder();
                foreach (var p in pins)
                {
                    if (!p.m_save) continue;
                    if (sb.Length > 0)
                    {
                        sb.Append(",");
                    }

                    var pd = new SavedPinData
                    {
                        Name = p.m_name,
                        Pos = p.m_pos,
                        Type = p.m_type.ToString(),
                        Checked = p.m_checked,
                        Animate = p.m_animate
                    };

                    var memoryStream = new MemoryStream();
                    var binaryFormatter = new BinaryFormatter();
                    binaryFormatter.Serialize(memoryStream, pd);
                    var outpd = Convert.ToBase64String(memoryStream.ToArray());

                    sb.Append(outpd);
                }

                return sb.ToString();
            }

            static string BuildMapString(bool[] mapData)
            {
                var blder = new StringBuilder(mapData.Length);
                foreach (var b in mapData)
                {
                    blder.Append(b ? "1" : "0");
                }

                return blder.ToString();
            }

            static string CompressString(string text)
            {
                var buffer = Encoding.UTF8.GetBytes(text);
                var memoryStream = new MemoryStream();
                using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
                {
                    gZipStream.Write(buffer, 0, buffer.Length);
                }

                memoryStream.Position = 0;

                var compressedData = new byte[memoryStream.Length];
                memoryStream.Read(compressedData, 0, compressedData.Length);

                var gZipBuffer = new byte[compressedData.Length + 4];
                Buffer.BlockCopy(compressedData, 0, gZipBuffer, 4, compressedData.Length);
                Buffer.BlockCopy(BitConverter.GetBytes(buffer.Length), 0, gZipBuffer, 0, 4);
                return Convert.ToBase64String(gZipBuffer);
            }

            public static void SetMapDataToMerge(string mapFrom, bool[] mapData, List<SavedPinData> savedPinDatas)
            {
                _mapSender = mapFrom;
                _pendingPinDataToMerge = savedPinDatas;
                _pendingMapDataToMerge = mapData;
                Debug.Log($"pending pins to merge: {_pendingPinDataToMerge.Count} _pendingMapDataToMerge: {(_pendingMapDataToMerge != null ? _pendingMapDataToMerge.Length.ToString() : "none")}");
            }

            static void MergePinData(List<SavedPinData> pinDatas, List<Minimap.PinData> mPins, Minimap minimap)
            {
                if (pinDatas == null || pinDatas.Count == 0) return;

                foreach (var p in pinDatas)
                {
                    if (!HaveSimilarPin(mPins, p))
                    {
                        minimap.AddPin(p.Pos, (Minimap.PinType) Enum.Parse(typeof(Minimap.PinType), p.Type), p.Name,
                            true, p.Checked);
                    }
                }
            }

            static void MergeMapData(bool[] myExploreData, Minimap minimap, Texture2D m_fogTexture)
            {
                try
                {
                    if (myExploreData.Length != _pendingMapDataToMerge.Length)
                    {
                        Debug.Log("MapSync::Map Data is different resolution!");
                        _pendingMapDataToMerge = null;
                        return;
                    }

                    var exploredChunkCounter = 0;
                    var ySize = minimap.m_textureSize;
                    Debug.Log($"MapSync::Texture size is {minimap.m_textureSize}");

                    for (var i = 0; i < _pendingMapDataToMerge.Length; i++)
                    {
                        if (!_pendingMapDataToMerge[i]) continue;
                        if (HookExplore.call_Explore(minimap, i % ySize, i / ySize))
                        {
                            exploredChunkCounter++;
                        }
                    }

                    if (exploredChunkCounter > 0)
                    {
                        m_fogTexture.Apply();
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("MapSync::An error occurred merging map data.");
                    Debug.LogException(e);
                }

                _pendingMapDataToMerge = null;
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

        [HarmonyPatch(typeof(Chat), "OnNewChatMessage")]
        static class OnNewChatMessage_Patch
        {
            static bool Prefix(string text)
            {
                //Catch chat message before it goes to chat window and check for MapData message
                Debug.Log($"Received RPC MapData message: Size: {text.Length}");
                if (!text.StartsWith("MapData")) return true;
                var splits = text.Split(':');

                if (splits.Length != 8)
                {
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                        "Data received from non-compatible Map Sharing Made Easy plugin version.", 0);
                    return false;
                }

                var sentFrom = splits[3];
                var sentTo = splits[6];
                var pluginVersion = splits[7];

                if (pluginVersion != PluginVersion)
                {
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                        $"Data received from non-same Map Sharing Made Easy plugin version - {pluginVersion} vs {PluginVersion}", 0);
                    return false;
                }
                
                if (!sentTo.Equals(Player.m_localPlayer.GetPlayerName())) return false;

                var decompressedMap = DecompressString(splits[4]);
                var decompressedPins = "";
                if (splits.Length > 4)
                {
                    decompressedPins = DecompressString(splits[5]);
                }

                Debug.Log(
                    $"MapSync::MapData received. \nMapFrom: {splits[3]}\nMap String Sent Length:{splits[1]}\nCompressed String Sent Length: {splits[2]}\nReceived Map String Length: {decompressedMap.Length}\nReceived Compressed String Length: {splits[4].Length}");

                if (!decompressedMap.Length.ToString().Equals(splits[1]))
                {
                    Debug.Log("MapSync::Corrupted map string received.");
                    return false;
                }

                bool[] mapData = null;
                if (decompressedMap.Length > 0)
                {
                    var mapChrArray = decompressedMap.ToCharArray();
                    mapData = new bool[mapChrArray.Length];
                    for (var i = 0; i < mapChrArray.Length; i++)
                    {
                        mapData[i] = mapChrArray[i] == '1';
                    }
                }

                var pins = new List<SavedPinData>();
                if (decompressedPins.Length > 0)
                {
                    deserializePins(decompressedPins, pins);
                }

                UpdateExplore_Patch.SetMapDataToMerge(sentFrom, mapData, pins);

                return false;
            }

            private static void deserializePins(string pinString, List<SavedPinData> pins)
            {
                if (pinString.Length <= 1) return;
                var splits = pinString.Split(',');
                foreach (var s in splits)
                {
                    var bytes = Convert.FromBase64String(s);
                    var bfd = new BinaryFormatter();
                    var ms = new MemoryStream(bytes);
                    var pin = bfd.Deserialize(ms) as SavedPinData;
                    pins.Add(pin);
                }
            }

            private static string DecompressString(string compressedText)
            {
                var gZipBuffer = Convert.FromBase64String(compressedText);
                using (var memoryStream = new MemoryStream())
                {
                    var dataLength = BitConverter.ToInt32(gZipBuffer, 0);
                    memoryStream.Write(gZipBuffer, 4, gZipBuffer.Length - 4);

                    var buffer = new byte[dataLength];

                    memoryStream.Position = 0;
                    using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                    {
                        gZipStream.Read(buffer, 0, buffer.Length);
                    }

                    return Encoding.UTF8.GetString(buffer);
                }
            }
        }
    }
    
    [Serializable]
    public struct SerializableVector3
    {
        public float x;
        public float y;
        public float z;

        public SerializableVector3(float rX, float rY, float rZ)
        {
            x = rX;
            y = rY;
            z = rZ;
        }

        public override string ToString()
        {
            return String.Format("[{0}, {1}, {2}]", x, y, z);
        }

        public static implicit operator Vector3(SerializableVector3 rValue)
        {
            return new Vector3(rValue.x, rValue.y, rValue.z);
        }

        public static implicit operator SerializableVector3(Vector3 rValue)
        {
            return new SerializableVector3(rValue.x, rValue.y, rValue.z);
        }
    }

    [Serializable]
    class SavedPinData
    {
        public string Name;
        public SerializableVector3 Pos;
        public string Type;
        public bool Checked;
        public bool Animate;

        public override string ToString()
        {
            return $"{Name}\t{Pos}\t{Type}\t{Checked}\t{Animate}";
        }
    }
}