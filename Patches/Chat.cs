using HarmonyLib;
using UnityEngine;

namespace MapSharingMadeEasy.Patches
{
    [HarmonyPatch(typeof(Chat), "OnNewChatMessage")]
    static class OnNewChatMessage_Patch
    {
        static bool Prefix(string text)
        {
            if (!text.StartsWith("MapData")) return true;
            
            var validData = MapData.ParseReceivedMapData(text, out var sentFrom, out var sentTo, out var pluginVersion,
                out var pins, out var mapData);
            if (validData)
            {
                Debug.Log($"Received map data from {sentFrom} via chat.");
                MapData.instance.SyncData = text;
                MapData.instance.MapSender = sentFrom;
            }
            else
            {
                Debug.Log("MapData was invalid, ignoring.");
            }

            return false;
        }
    }
}