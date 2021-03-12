using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MapSharingMadeEasy.Patches
{
    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    static class UpdatePlacementGhost_Patch
    {
        static void Postfix(Player __instance,
            GameObject ___m_placementGhost, ref PlacementStatus ___m_placementStatus, ref Piece ___m_hoveringPiece, ref int ___m_placeRayMask)
        {
            if (___m_placementGhost == null) return;

            Vector3 point;
            Vector3 normal;
            Piece piece;
            Heightmap heightmap;
            Collider waterSurface;
            HookPieceRayTest.call_PieceRayTest(__instance, out point, out normal, out piece, out heightmap,
                out waterSurface, false);
            
            if (piece != null && piece.name == "LargeMap")
            {
                var wnt = piece.GetComponent<WearNTear>();
                wnt.Highlight();
                ___m_hoveringPiece = piece;
            }
            
            if (!___m_placementGhost.name.Equals("LargeMap")) return;

            if (piece == null || piece.m_category != Piece.PieceCategory.Furniture ||
                !piece.m_name.Contains("table"))
            {
                ___m_placementGhost.transform.position = point;
                ___m_placementStatus = PlacementStatus.Invalid;
                ___m_placementGhost.GetComponent<Piece>().SetInvalidPlacementHeightlight(true);
                return;
            }

            ___m_placementGhost.transform.position =
                new Vector3(piece.transform.position.x, point.y, piece.transform.position.z);
            ___m_placementGhost.transform.rotation = piece.transform.rotation;
            ___m_placementStatus = PlacementStatus.Valid;
            ___m_placementGhost.GetComponent<Piece>().SetInvalidPlacementHeightlight(false);
        }
        
        [HarmonyPatch(typeof(Player))]
        private class HookPieceRayTest
        {
            [HarmonyReversePatch]
            [HarmonyPatch(typeof(Player), "PieceRayTest")]
            public static bool call_PieceRayTest(object instance, out Vector3 point, out Vector3 normal,
                out Piece piece, out Heightmap heightmap, out Collider waterSurface, bool water) =>
                throw new NotImplementedException();
        }
        
        [HarmonyPatch(typeof(Player), "UpdateKnownRecipesList")]
        static class UpdatePlayerRecipes_Patch
        {
            static bool Prefix(Player __instance)
            {
                var items = __instance.GetInventory().GetAllItems();
                foreach (var itemData in items)
                {
                    if (itemData.m_shared.m_name.Contains("hammer"))
                    {
                        if (Utils.AddMPieceToPieceTable(itemData.m_shared.m_buildPieces.m_pieces, SharedMap.MapPrefab))
                        {
                            var known = new HashSet<string>();
                            known.Add(SharedMap.MapPrefab.name);
                            itemData.m_shared.m_buildPieces.UpdateAvailable(known, __instance, false, true);
                        }
                    }
                }

                return true;
            }
        }
        
        private enum PlacementStatus
        {
            Valid,
            Invalid,
            BlockedbyPlayer,
            NoBuildZone,
            PrivateZone,
            MoreSpace,
            NoTeleportArea,
            ExtensionMissingStation,
            WrongBiome,
            NeedCultivated,
            NotInDungeon,
        }
    }
}