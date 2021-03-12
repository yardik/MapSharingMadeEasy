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
using Random = UnityEngine.Random;

namespace MapSharingMadeEasy
{
    [BepInPlugin("yardik.MapSharingMadeEasy", "Map Sharing Made Easy", "2.0.0")]
    public class MapSharingMadeEasy : BaseUnityPlugin
    {
        public string PluginVersion = "";
        public static MapSharingMadeEasy instance;
        private static ConfigEntry<bool> modEnabled;
        private static AssetBundle _assetBundle;
        private MapData _mapData;

        private void Initialize()
        {
            Debug.Log($"Start debug");
            var mapPrefab = (GameObject) _assetBundle.LoadAsset("TrophyMap", typeof(GameObject));
            var trophySprite = (Sprite) _assetBundle.LoadAsset("TrophySprite", typeof(Sprite));
            Debug.Log($"TrophySprite: {trophySprite}");

            mapPrefab.layer = 10;
            
            Debug.Log("Adding1");
            var znet = mapPrefab.AddComponent<ZNetView>();
            znet.m_persistent = true;
            znet.m_distant = false;
            znet.m_type = ZDO.ObjectType.Solid;
            znet.m_syncInitialScale = false;

            var piece = mapPrefab.AddComponent<Piece>();
            piece.m_primaryTarget = false;
            piece.m_randomTarget = false;
            piece.m_icon = trophySprite;
            piece.name = "LargeMap";
            piece.m_category = Piece.PieceCategory.Furniture;
            piece.m_comfort = 0;
            piece.m_comfortGroup = Piece.ComfortGroup.None;
            piece.m_noInWater = true;
            piece.m_notOnTiltingSurface = true;
            piece.m_noClipping = true;
            piece.m_canBeRemoved = true;

            var wearntear = mapPrefab.AddComponent<WearNTear>();
            wearntear.m_broken = mapPrefab;
            wearntear.m_new = mapPrefab;
            wearntear.m_worn = mapPrefab;
            wearntear.m_damages = new HitData.DamageModifiers();
            wearntear.m_health = 100;
            wearntear.m_noRoofWear = false;
            wearntear.m_noSupportWear = false;
            wearntear.m_materialType = WearNTear.MaterialType.Wood;
            wearntear.m_supports = false;
            wearntear.m_comOffset = new Vector3(0f, 0.25f, 0f);

            var sharedMap = mapPrefab.AddComponent<SharedMap>();
            Debug.Log($"Map prefab is: {piece.name}");
            sharedMap.SetMapData("");
            sharedMap.transform.position = Vector3.zero;
            SharedMap.MapPrefab = mapPrefab;
            _mapData = new MapData();
        }

        private void Awake()
        {
            var execAssembly = Assembly.GetExecutingAssembly();
            var names = execAssembly.GetManifestResourceNames();

            var resName = names.First(n => n.Contains("mapsharingmadeeasy"));
            var stream = execAssembly.GetManifestResourceStream(resName);
            _assetBundle = AssetBundle.LoadFromStream(stream);

            Initialize();

            var bpp = (BepInPlugin) GetType().GetCustomAttributes(typeof(BepInPlugin), true)[0];
            PluginVersion = bpp.Version.ToString();
            Debug.Log($"MapSharingMadeEasy Version: {PluginVersion}");
            instance = this;
            modEnabled = Config.Bind("General", "Enabled", true, "Enable this mod");
            Settings.Init(Config);

            if (!modEnabled.Value)
                return;

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }
    }
}