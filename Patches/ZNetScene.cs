using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MapSharingMadeEasy.Patches
{
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    static class UpdateZNetScene_Prefabs
    {
        static void Postfix(ZNetScene __instance, Dictionary<int, GameObject> ___m_namedPrefabs)
        {
            __instance.m_prefabs.Add(SharedMap.MapPrefab);
            ___m_namedPrefabs.Add(Utils.GetStableHashCode(SharedMap.MapPrefab.name), SharedMap.MapPrefab);
        }
    }
}