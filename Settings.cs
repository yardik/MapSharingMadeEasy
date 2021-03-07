using BepInEx.Configuration;
using UnityEngine;

namespace ValheimMapMod
{
    public class Settings
    {
        public static void Init(ConfigFile config)
        {
            MapSettings.Init(config);
        }

        public class MapSettings
        {
            public static ConfigEntry<KeyCode> SyncMapKey { get; private set; }
            public static ConfigEntry<KeyCode> AcceptMapKey { get; private set; }
            public static ConfigEntry<KeyCode> RejectMapKey { get; private set; }
            public static ConfigEntry<float> SkipPinRange { get; private set; }
            public static void Init(ConfigFile config)
            {
                string name = "MapSettings";
                SyncMapKey = config.Bind(name, "SyncMapKey", KeyCode.F9,
                    "What key to press to send map to target?");

                AcceptMapKey = config.Bind(name, "AcceptMapKey", KeyCode.F7,
                    "What key to press to accept a sent map?");

                RejectMapKey = config.Bind(name, "RejectMapKey", KeyCode.F8,
                    "What key to press to reject a sent map?");

                SkipPinRange = config.Bind(name, "SkipPinRange", 25f,
                    "How close are pins before the incoming one is ignored during a merge?");
                Debug.Log($"Loaded settings!\nSyncMapKey: {SyncMapKey.Value}\nAcceptMapKey:{AcceptMapKey.Value}\nRejectMapKey:{RejectMapKey.Value}\nSkipPinRange:{SkipPinRange.Value}");
            }
        }
    }
}