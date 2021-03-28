using HarmonyLib;
using UnityEngine;

namespace MapSharingMadeEasy.Patches
{
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    static class ObjectDB_Patch
    {
        static void Postfix(ObjectDB __instance)
        {
            if (__instance.m_items.Count == 0 || __instance.GetItemPrefab("Amber") == null)
            {
                Utils.Log("Waiting for game to initialize before adding prefabs.");
                return;
            }

            MapSharingMadeEasy.instance.TryRegisterItems();
        }
    }
}