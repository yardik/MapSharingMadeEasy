using System;
using UnityEngine;

namespace MapSharingMadeEasy
{
    [Serializable]
    public class PlayerSyncData
    {
        public string Name { get; }
        public DateTime SyncDate { get; }
        
        public PlayerSyncData(string name, DateTime now)
        {
            Name = name;
            SyncDate = now;
        }
    }
}