using System;
using UnityEngine;

namespace MapSharingMadeEasy
{
    public class SharedMap : MonoBehaviour, Hoverable, Interactable
    {
        public static GameObject MapPrefab;
        public string m_name = nameof (SharedMap);
        public float m_useDistance = 2f;        
        private const float m_minSitDelay = 2f;
        private string _mapData;
        private ZNetView m_nview;

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            if (m_nview.GetZDO() == null)
                return;
            UpdateMapData();
        }

        private void UpdateMapData()
        {
            var mapData = GetMapData();
            if (_mapData == mapData)
                return;
            _mapData = mapData;
        }

        public string GetMapData() => m_nview.GetZDO().GetString("mapData", "");
        
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
            
            Debug.Log($"Synchronizing map! Found {_mapData.Length} bytes");
            MapData.instance.SyncWith = this;
            return false;
        }

        private bool InUseDistance(Humanoid human) => Vector3.Distance(human.transform.position, transform.position) < (double) m_useDistance;

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public void SetMapData(string mapData)
        {
            if (m_nview == null)
                return;
            
            m_nview.ClaimOwnership();
            m_nview.GetZDO().Set(nameof(mapData), mapData);
            _mapData = mapData;
        }
    }
}