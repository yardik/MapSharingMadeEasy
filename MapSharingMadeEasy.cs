using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MapSharingMadeEasy
{
    class PieceInfo
    {
        public string PieceName;
        public GameObject Prefab;
        public string PieceTable;
        public string CraftingStation;
        public Dictionary<string, int> Resources;
    }

    [BepInPlugin("yardik.MapSharingMadeEasy", "Map Sharing Made Easy", "2.4.0")]
    public class MapSharingMadeEasy : BaseUnityPlugin
    {
        private Dictionary<string, PieceInfo> PiecesToRegister = new Dictionary<string, PieceInfo>();
        public static string PluginName = "none";
        public string PluginVersion = "none";
        public static MapSharingMadeEasy instance;
        private static ConfigEntry<bool> modEnabled;
        private static ConfigEntry<int> nexusID;
        private AssetBundle _assetBundle;
        private MapData _mapData;

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
            PluginName = bpp.Name;
            Utils.Log($"MapSharingMadeEasy Version: {PluginVersion}");
            instance = this;
            modEnabled = Config.Bind("General", "Enabled", true, "Enable this mod");
            nexusID = Config.Bind("General", "NexusID", 300, "Nexus mod ID for updates");
            Settings.Init(Config);

            if (!modEnabled.Value)
                return;

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }

        private void Initialize()
        {
            Utils.Log($"Start debug");
            var mapPrefab = (GameObject) _assetBundle.LoadAsset("LargeMap", typeof(GameObject));
            var trophySprite = (Sprite) _assetBundle.LoadAsset("LargeMapSprite", typeof(Sprite));
            Utils.Log($"TrophySprite: {trophySprite}");

            var sharedMap = mapPrefab.AddComponent<SharedMap>();
            Utils.Log($"Map prefab is: {mapPrefab.name}");
            sharedMap.SetMapData("");
            sharedMap.transform.position = Vector3.zero;
            SharedMap.MapPrefab = mapPrefab;
            var wnt = mapPrefab.GetComponent<WearNTear>();
            if (wnt)
                wnt.m_noSupportWear = false;

            _mapData = new MapData();
            var pi = new PieceInfo
            {
                PieceName = mapPrefab.name,
                Prefab = mapPrefab,
                PieceTable = "_HammerPieceTable",
                CraftingStation = "piece_workbench",
                Resources = new Dictionary<string, int> {{"DeerHide", 3}}
            };

            PiecesToRegister.Add(mapPrefab.name, pi);
        }

        public static bool IsObjectDBReady()
        {
            // Hack, just making sure the built-in items and prefabs have loaded
            return ObjectDB.instance != null && ObjectDB.instance.m_items.Count != 0 &&
                   ObjectDB.instance.GetItemPrefab("Amber") != null;
        }

        public void TryRegisterItems()
        {
            if (!IsObjectDBReady())
            {
                return;
            }

            foreach (var prefab in PiecesToRegister)
            {
                var itemDrop = prefab.Value.Prefab.GetComponent<ItemDrop>();
                if (itemDrop != null)
                {
                    if (ObjectDB.instance.GetItemPrefab(Utils.GetStableHashCode(prefab.Key)) == null)
                    {
                        ObjectDB.instance.m_items.Add(prefab.Value.Prefab);
                    }
                }
            }

            var pieceTables = new List<PieceTable>();
            foreach (var itemPrefab in ObjectDB.instance.m_items)
            {
                var item = itemPrefab.GetComponent<ItemDrop>().m_itemData;
                if (item.m_shared.m_buildPieces != null && !pieceTables.Contains(item.m_shared.m_buildPieces))
                {
                    pieceTables.Add(item.m_shared.m_buildPieces);
                }
            }

            var craftingStations = new List<CraftingStation>();
            foreach (var pieceTable in pieceTables)
            {
                craftingStations.AddRange(pieceTable.m_pieces
                    .Where(x => x.GetComponent<CraftingStation>() != null)
                    .Select(x => x.GetComponent<CraftingStation>()));
            }

            TryRegisterPieces(pieceTables, craftingStations);
        }

        public void TryRegisterPieces(List<PieceTable> pieceTables, List<CraftingStation> craftingStations)
        {
            foreach (var entry in PiecesToRegister)
            {
                var prefab = entry.Value.Prefab;
                if (prefab == null)
                {
                    Debug.LogError($"Tried to register piece but prefab was null!");
                    continue;
                }

                var piece = prefab.GetComponent<Piece>();
                if (piece == null)
                {
                    Debug.LogError($"Tried to register piece ({prefab}) but Piece component was missing!");
                    continue;
                }

                var pieceTable = pieceTables.Find(x => x.name == entry.Value.PieceTable);
                if (pieceTable == null)
                {
                    Debug.LogError(
                        $"Tried to register piece ({prefab}) but could not find piece table {entry.Value.PieceTable} (pieceTables({pieceTables.Count})= {string.Join(", ", pieceTables.Select(x => x.name))})!");
                    continue;
                }

                if (pieceTable.m_pieces.Contains(prefab))
                {
                    continue;
                }

                pieceTable.m_pieces.Add(prefab);

                var pieceStation = craftingStations.Find(x => x.name == entry.Value.CraftingStation);
                piece.m_craftingStation = pieceStation;

                var resources = new List<Piece.Requirement>();
                foreach (var resource in entry.Value.Resources)
                {
                    var resourcePrefab = ObjectDB.instance.GetItemPrefab(resource.Key);
                    resources.Add(new Piece.Requirement()
                    {
                        m_resItem = resourcePrefab.GetComponent<ItemDrop>(),
                        m_amount = resource.Value
                    });
                }

                piece.m_resources = resources.ToArray();

                var otherPiece = pieceTable.m_pieces.Find(x => x.GetComponent<Piece>() != null).GetComponent<Piece>();
                piece.m_placeEffect.m_effectPrefabs.AddRangeToArray(otherPiece.m_placeEffect.m_effectPrefabs);
            }
        }
    }
}
