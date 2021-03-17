using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MapSharingMadeEasy.Patches
{
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    static class ObjectDB_Patch
    {
        static void Postfix(ObjectDB __instance, Dictionary<int, GameObject> ___m_itemByHash)
        {
            if (__instance.m_items.Count == 0 || __instance.GetItemPrefab("Amber") == null)
            {
                Debug.Log("Waiting for game to initialize before adding prefabs.");
                return;
            }
            MapSharingMadeEasy.instance.TryRegisterItems();
        }
    }
}