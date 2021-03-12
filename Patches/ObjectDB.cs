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
            Debug.Log("Adding _mapPrefab to ObjectDB");
            var addedInstance = false;

            foreach (var instanceMItem in __instance.m_items)
            {
                if (instanceMItem.name.Equals(SharedMap.MapPrefab.name))
                {
                    addedInstance = true;
                    break;
                }
            }

            if (!addedInstance)
            {
                __instance.m_items.Add(SharedMap.MapPrefab);
                ___m_itemByHash.Add(Utils.GetStableHashCode(SharedMap.MapPrefab.gameObject.name), SharedMap.MapPrefab.gameObject);
            }

            foreach (var instanceMItem in __instance.m_items)
            {
                if (!instanceMItem.name.Equals("Hammer")) continue;

                var mapPrefabPiece = SharedMap.MapPrefab.GetComponent<Piece>();
                if (mapPrefabPiece == null)
                {
                    Debug.Log("mapPrefab has no piece.");
                    return;
                }

                Debug.Log("Found Hammer - Adding map recipes");
                if (instanceMItem == null)
                {
                    Debug.Log("instanceMItem is null");
                }

                var itemDropHammer = instanceMItem.GetComponent<ItemDrop>();
                Utils.AddMPieceToPieceTable(itemDropHammer.m_itemData.m_shared.m_buildPieces.m_pieces, SharedMap.MapPrefab);
            }
        }
    }
}