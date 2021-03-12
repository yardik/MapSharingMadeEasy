using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using MapSharingMadeEasy.Patches;
using UnityEngine;

namespace MapSharingMadeEasy
{
    public class MapData
    {
        public static MapData instance;
        public string SyncData;
        public SharedMap SyncWith { get; set; }
        public string MapSender { get; set; }

        public MapData()
        {
            instance = this;
        }

        public static bool ParseReceivedMapData(string text, out string sentFrom, out string sentTo,
            out string pluginVersion, out List<SavedPinData> pins, out bool[] mapData)
        {
            Debug.Log($"Received MapData message: Size: {text.Length}");
            pins = new List<SavedPinData>();
            mapData = new bool[0];
            sentFrom = "";
            sentTo = "";
            pluginVersion = "";
            
            var splits = text.Split(':');

            if (splits.Length != 8)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                    "Data received from non-compatible Map Sharing Made Easy plugin version.", 0);
                Debug.Log("Invalid plugin version of map data.");
                return false;
            }

            sentFrom = splits[3];
            sentTo = splits[6];
            pluginVersion = splits[7];

            if (pluginVersion != MapSharingMadeEasy.instance.PluginVersion)
            {
                Debug.Log($"Data received from non-same Map Sharing Made Easy plugin version - {pluginVersion} vs {MapSharingMadeEasy.instance.PluginVersion}");
                Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                    $"Data received from non-same Map Sharing Made Easy plugin version - {pluginVersion} vs {MapSharingMadeEasy.instance.PluginVersion}",
                    0);
                return false;
            }

            if (!sentTo.Equals(Player.m_localPlayer.GetPlayerName()) && !sentTo.Equals("AnyPlayer"))
            {
                Debug.Log("This data is for another player.");
                return false;
            }

            var decompressedMap = Utils.DecompressString(splits[4]);
            var decompressedPins = "";
            if (splits.Length > 4)
            {
                decompressedPins = Utils.DecompressString(splits[5]);
            }

            Debug.Log(
                $"MapSync::MapData received. \nMapFrom: {splits[3]}\nMap String Sent Length:{splits[1]}\nCompressed String Sent Length: {splits[2]}\nReceived Map String Length: {decompressedMap.Length}\nReceived Compressed String Length: {splits[4].Length}");

            if (!decompressedMap.Length.ToString().Equals(splits[1]))
            {
                Debug.Log("MapSync::Corrupted map string received.");
                return false;
            }

            mapData = null;
            if (decompressedMap.Length > 0)
            {
                var mapChrArray = decompressedMap.ToCharArray();
                mapData = new bool[mapChrArray.Length];
                for (var i = 0; i < mapChrArray.Length; i++)
                {
                    mapData[i] = mapChrArray[i] == '1';
                }
            }

            pins = new List<SavedPinData>();
            if (decompressedPins.Length > 0)
            {
                DeserializePins(decompressedPins, pins);
            }

            return true;
        }

        private static void DeserializePins(string pinString, List<SavedPinData> pins)
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

        public void ClearPendingSync()
        {
            SyncData = "";
            SyncWith = null;
            MapSender = "";
        }

        public static string GetMapDataString(string fromPlayerName, string toPlrName, bool sendingMap,
            bool sendingPins, bool[] exploredData, List<Minimap.PinData> pins)
        {
            var mapString = "";
            var pinString = "";

            if (sendingMap)
                mapString = BuildMapString(exploredData);

            if (Settings.MapSettings.SendPinShares.Value || sendingPins)
                pinString = BuildPinString(pins);

            var compressedMapString = CompressString(mapString);
            var compressedPinString = CompressString(pinString);

            return
                $"MapData:{mapString.Length}:{compressedMapString.Length}:{fromPlayerName}:{compressedMapString}:{compressedPinString}:{toPlrName}:{MapSharingMadeEasy.instance.PluginVersion}";
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

        private void SetMapDataToMerge(string mapData)
        {
            ParseReceivedMapData(mapData, out var sentFrom, out var sentTo, out var pluginVersion, out var pins,
                out var mapDataOut);
            SyncData = mapData;
            MapSender = sentFrom;
            Debug.Log(
                $"Setting pending map data from {MapSender} to merge.");
        }

        public static int MergeMapData(bool[] myExploreData, bool[] pendingMapDataToMerge, out bool[] mergedData)
        {
            var mergedChunks = 0;
            mergedData = new bool[myExploreData.Length];
            if (myExploreData.Length != pendingMapDataToMerge.Length)
            {
                Debug.Log(
                    $"MapSync::Map Data is different resolution myExploreData:{myExploreData.Length} vs pendingMapDataToMerge:{pendingMapDataToMerge.Length}!");
                return 0;
            }

            for (var i = 0; i < pendingMapDataToMerge.Length; i++)
            {
                mergedData[i] = pendingMapDataToMerge[i] || myExploreData[i];
                if (mergedData[i] != myExploreData[i])
                {
                    mergedChunks++;
                }
            }

            return mergedChunks;
        }

        public static bool ExploreMap(Minimap minimap, Texture2D m_fogTexture, bool[] mapData)
        {
            try
            {
                var exploredChunkCounter = 0;
                var ySize = minimap.m_textureSize;
                Debug.Log($"MapSync::Texture size is {minimap.m_textureSize}");

                for (var i = 0; i < mapData.Length; i++)
                {
                    if (!mapData[i]) continue;
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
                return false;
            }

            return true;
        }

        public static bool MergeWithSharedMap(Player player, Minimap minimap,
            Texture2D fogTexture, SharedMap syncWith, List<Minimap.PinData> myPins, bool[] myMapData)
        {
            var mergedData = new bool[myMapData.Length];
            if (syncWith == null)
                return false;

            var syncMapData = syncWith.GetMapData();
            Debug.Log($"Map Data Size: {syncMapData.Length}");
            ParseReceivedMapData(syncMapData, out var sentFrom, out var sentTo, out var pluginVersion,
                out var sharedPins, out var sharedMapData);
            if (sharedMapData != null)
            {
                MergeMapData(myMapData, sharedMapData, out mergedData);
                ExploreMap(minimap, fogTexture, mergedData);
            }

            //TODO: add check for pin sharing
            var sharedMapPins = new List<Minimap.PinData>();
            sharedMapPins.AddRange(CreatePinsFromSavedPinData(sharedPins));
            if (sharedPins != null && Settings.MapSettings.AcceptPinShares.Value)
            {
                Debug.Log($"Merging {sharedMapPins.Count} sharedMapPins with my {myPins.Count} pins");
                MergePinData(sharedMapPins, myPins, minimap);
                Debug.Log($"Total pins now: {myPins.Count}");
            }

            if (Settings.MapSettings.SendPinShares.Value && Settings.MapSettings.AcceptPinShares.Value)
            {
                sharedMapPins = myPins;
            }
            
            syncWith.SetMapData(GetMapDataString(player.GetPlayerName(), "AnyPlayer", true, true, mergedData, sharedMapPins));

            player.Message(MessageHud.MessageType.Center,
                $"You copy the map and pins, then update it with your own discoveries.");

            return true;
        }

        public static List<Minimap.PinData> CreatePinsFromSavedPinData(List<SavedPinData> sharedPins)
        {
            if (sharedPins == null || sharedPins.Count == 0)
            {
                return new List<Minimap.PinData>();
            }

            return sharedPins.ConvertAll(p => CreatePin(p.Pos,
                (Minimap.PinType) Enum.Parse(typeof(Minimap.PinType), p.Type),
                p.Name,
                true, p.Checked));
        }

        public static void MergePinData(List<Minimap.PinData> pinsIn, List<Minimap.PinData> existingPins, Minimap minimap)
        {
            if (pinsIn == null || pinsIn.Count == 0) return;

            foreach (var p in pinsIn)
            {
                if (!HaveSimilarPin(existingPins, p))
                {
                    minimap.AddPin(p.m_pos, p.m_type, p.m_name, p.m_save, p.m_checked);
                }
            }
        }

        public static Minimap.PinData CreatePin(
            Vector3 pos,
            Minimap.PinType type,
            string name,
            bool save,
            bool isChecked)
        {
            Minimap.PinData pinData = new Minimap.PinData();
            pinData.m_type = type;
            pinData.m_name = name;
            pinData.m_pos = pos;
            pinData.m_save = save;
            pinData.m_checked = isChecked;
            return pinData;
        }

        private static bool HaveSimilarPin(List<Minimap.PinData> pinData, Minimap.PinData sp)
        {
            return pinData.Any(pin =>
            {
                var dist = DistanceXZ(sp.m_pos, pin.m_pos);
                return dist < Settings.MapSettings.SkipPinRange.Value;
            });
        }

        private static float DistanceXZ(Vector3 v0, Vector3 v1)
        {
            var num1 = v1.x - (double) v0.x;
            var num2 = v1.z - v0.z;
            return Mathf.Sqrt((float) (num1 * num1 + num2 * (double) num2));
        }
    }
}