using HarmonyLib;
using UnityEngine;

namespace MapSharingMadeEasy.Patches
{
    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    static class Player_Patch
    {
        public static Vector3 Axis = Vector3.up;
        static void Postfix(Player __instance)
        {
            if (__instance.m_placementGhost == null || __instance.m_placementStatus != Player.PlacementStatus.Valid)
                return;
            
            Piece component1 = __instance.m_placementGhost.GetComponent<Piece>();
            if (component1 == null)
                return;
            
            bool water = component1.m_waterPiece || component1.m_noInWater;
            if (!component1.name.Equals("LargeMap")) return;
            if (__instance.PieceRayTest(out var point, out var normal, out var piece, out var heightmap,
                out var waterSurface, water))
            {
                if (piece != null)
                {
                    bool wall = normal.y < 0.800000011920929;
                    var rot = Quaternion.Euler(wall ? 90f : 0f, 22.5f * (float) __instance.m_placeRotation, 0f);
                    normal *= 0.01f;
                    point += normal;
                    __instance.m_placementGhost.transform.position = point;
                    __instance.m_placementGhost.transform.rotation = rot;
                }
            }
        }
    }
}