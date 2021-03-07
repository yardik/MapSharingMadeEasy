using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ValheimMapMod
{
    [BepInPlugin("yardik.MapSharingMadeEasy", "Map Sharing Made Easy", "1.2.0")]
    public class ValheimMapMod : BaseUnityPlugin
    {
        private static ValheimMapMod context;
        public static ConfigEntry<bool> modEnabled;

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
        private class SavedPinData
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
        
        [HarmonyPatch(typeof(Minimap))]
        public class HookExplore
        {
            [HarmonyReversePatch]
            [HarmonyPatch(typeof(Minimap), "Explore", new Type[] { typeof(int), typeof(int) })]
            public static bool call_Explore(object instance, int x, int y) => throw new NotImplementedException();
        }

        private void Awake()
        {
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
            
            static void Postfix(Player player, bool[] ___m_explored, Minimap __instance, Texture2D ___m_fogTexture, List<Minimap.PinData> ___m_pins)
            {
                if (_pendingMapDataToMerge != null)
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
                            $"{_mapSender} wants to share their map ({Settings.MapSettings.AcceptMapKey.Value.ToString()} to accept, {Settings.MapSettings.RejectMapKey.Value.ToString()} to decline).");
                        _stopwatch = new Stopwatch();
                        _stopwatch.Start();
                        return;
                    }
                }

                //Decline map data
                if (Input.GetKeyDown(Settings.MapSettings.RejectMapKey.Value))
                {
                    Debug.Log($"MapSync::Declined request from {_mapSender} to merge maps.");
                    player.Message(MessageHud.MessageType.Center,
                        $"You decline the request from {_mapSender} to share their map with you.");
                    _pendingMapDataToMerge = null;
                    _pendingPinDataToMerge = null;
                    _mapSender = "";
                    return;
                }
                
                //Accept map data
                if (Input.GetKeyDown(Settings.MapSettings.AcceptMapKey.Value))
                {
                    if (_pendingMapDataToMerge != null)
                    {
                        Debug.Log($"MapSync::{_mapSender}'s map received and will be merged.");
                        player.Message(MessageHud.MessageType.Center,
                            $"You copy {_mapSender}'s map.");
                        
                        if (Settings.MapSettings.AcceptPinShares.Value)
                            MergePinData(_pendingPinDataToMerge, ___m_pins, __instance);
                        
                        MergeMapData(___m_explored, __instance, ___m_fogTexture);
                        _pendingMapDataToMerge = null;
                        _pendingPinDataToMerge = null;
                        _mapSender = null;
                    }    
                    return;
                }
                
                //If the minimap is open and you hit F9 - send map data via chat
                if (!Minimap.IsOpen()) return;
                if (Input.GetKeyDown(Settings.MapSettings.SyncMapKey.Value))
                {
                    Debug.Log("MapSync::Sending map to character nearby.");
                    var chars = new List<Character>();
                    Player.GetCharactersInRange(player.transform.position, 2f, chars);
                    chars.RemoveAll(c => !c.IsPlayer() || ((Player)c).GetPlayerName() == player.GetPlayerName());
                    if (chars.Count > 0)
                    {
                        var toPlr = ((Player) chars[0]);
                        var toPlrName = toPlr.GetPlayerName().Replace("[", "").Replace("]", "");
                        Debug.Log($"MapSync::Sending map to {toPlr.GetPlayerName()}");
                        player.Message(MessageHud.MessageType.Center, $"You let {toPlrName} copy your map.");
                        var mapString = BuildMapString(___m_explored);
                        var pinString = "";
                        
                        if (Settings.MapSettings.SendPinShares.Value)
                            pinString = BuildPinString(___m_pins);
                        
                        var compressedMapString = CompressString(mapString);
                        var compressedPinString = CompressString(pinString);
                        
                        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ChatMessage", (object) player.transform.position, (object) 2, (object) player.GetPlayerName(), (object) $"MapData:{mapString.Length}:{compressedMapString.Length}:{player.GetPlayerName()}:{compressedMapString}:{compressedPinString}");
                    }             
                }
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
                    Debug.Log($"Pin X({pin.m_name}) distance to Pin Y({sp.Name}: {dist}");
                    return  dist < Settings.MapSettings.SkipPinRange.Value;
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
            }

            static void MergePinData(List<SavedPinData> pinDatas, List<Minimap.PinData> mPins, Minimap minimap)
            {
                if (pinDatas == null || pinDatas.Count == 0) return;
                
                foreach (var p in  pinDatas)
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
        }

        [HarmonyPatch(typeof(Chat), "OnNewChatMessage")]
        static class OnNewChatMessage_Patch
        {
            static bool Prefix(string text)
            {
                //Catch chat message before it goes to chat window and check for MapData message
                Debug.Log($"Received RPC MapData message: {text}");
                if (!text.StartsWith("MapData")) return true;
                var splits = text.Split(':');
                var sentFrom = splits[3];
                var decompressedMap = DecompressString(splits[4]);
                var decompressedPins = "";
                if (splits.Length > 4)
                {
                    decompressedPins = DecompressString(splits[5]);
                }

                Debug.Log($"MapSync::MapData received. \nMapFrom: {splits[3]}\nMap String Sent Length:{splits[1]}\nCompressed String Sent Length: {splits[2]}\nReceived Map String Length: {decompressedMap.Length}\nReceived Compressed String Length: {splits[4].Length}");
                
                if (!decompressedMap.Length.ToString().Equals(splits[1]))
                {
                    Debug.Log("MapSync::Corrupted map string received.");
                    return false;
                }
                
                var mapChrArray = decompressedMap.ToCharArray();
                var mapData = new bool[mapChrArray.Length];
                for (var i = 0; i < mapChrArray.Length;i++)
                {
                    mapData[i] = mapChrArray[i] == '1';
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
}