using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace MapSharingMadeEasy
{
    public class SharedMap : MonoBehaviour, Hoverable, Interactable
    {
        public static GameObject MapPrefab;
        public string m_name = nameof(SharedMap);
        public float m_useDistance = 2f;
        private const float m_minSitDelay = 2f;
        private string _mapData;
        public Dictionary<string,ExtendedPinData> ExtendedPinData { get; private set; }
        public Dictionary<string, PlayerSyncData> PlayerSyncData { get; private set; }
        private ZNetView m_nview;

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            if (m_nview.GetZDO() == null)
                return;
            UpdateMapDataFromZDO();
        }

        private void UpdateMapDataFromZDO()
        {
            _mapData = GetMapData();
            var psd = GetPlayerSyncData();
            PlayerSyncData = DeserializeSyncData(psd);
            
            var epd = GetExtendedPinData();
            ExtendedPinData = DeserializeExtendedPinData(epd);
        }

        private Dictionary<string, PlayerSyncData> DeserializeSyncData(string syncdata)
        {
            if (string.IsNullOrEmpty(syncdata))
                return new Dictionary<string, PlayerSyncData>();
            
            var bytes = Convert.FromBase64String(syncdata);
            var bfd = new BinaryFormatter();
            var ms = new MemoryStream(bytes);
            var sd = bfd.Deserialize(ms) as Dictionary<string, PlayerSyncData>;
            return sd;
        }
        
        private Dictionary<string, ExtendedPinData> DeserializeExtendedPinData(string pindata)
        {
            if (string.IsNullOrEmpty(pindata))
                return new Dictionary<string, ExtendedPinData>();
            
            var bytes = Convert.FromBase64String(pindata);
            var bfd = new BinaryFormatter();
            var ms = new MemoryStream(bytes);
            var pd = bfd.Deserialize(ms) as Dictionary<string, ExtendedPinData>;
            return pd;
        }
        
        public string GetMapData() => m_nview.GetZDO().GetString("mapData", "");
        public string GetPlayerSyncData() => m_nview.GetZDO().GetString("playerSyncData", "");
        public string GetExtendedPinData() => m_nview.GetZDO().GetString("extendedPinData", "");
        
        public string GetHoverText()
        {
            return !InUseDistance(Player.m_localPlayer) ? "Too far to copy map." : "Copy map from table.";
        }

        public string GetHoverName() => this.m_name;

        public bool Interact(Humanoid human, bool hold)
        {
            if (hold)
                return false;
            Player player = human as Player;
            if (!InUseDistance(player))
                return false;

            Utils.Log($"Synchronizing map! Found {_mapData.Length} bytes");
            UpdateMapDataFromZDO();
            MapData.instance.SyncWith = this;
            return false;
        }

        private bool InUseDistance(Humanoid human) =>
            Vector3.Distance(human.transform.position, transform.position) < (double) m_useDistance;

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public void SetMapData(string mapData)
        {
            if (m_nview == null)
                return;

            m_nview.ClaimOwnership();
            m_nview.GetZDO().Set(nameof(mapData), mapData);
            _mapData = mapData;
            Utils.Log("Writing Shared Map Data");
        }
        
        public void UpdatePlayerSyncData()
        {
            if (m_nview == null)
                return;

            var playerSyncData = PlayerSyncData.SerializeList();
            
            m_nview.ClaimOwnership();
            m_nview.GetZDO().Set(nameof(playerSyncData), playerSyncData);
            Utils.Log("Writing PlayerSyncData");
        }
        
        public void UpdateExtendedPinData()
        {
            if (m_nview == null)
                return;

            var extendedPinData = ExtendedPinData.SerializeList();
            
            m_nview.ClaimOwnership();
            m_nview.GetZDO().Set(nameof(extendedPinData), extendedPinData);
            Utils.Log("Writing ExtendedPinData");
        }
    }
}