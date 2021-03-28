using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace MapSharingMadeEasy
{
    public class Settings
    {
        public static void Init(ConfigFile config)
        {
            MapSettings.Init(config);
        }

        public class MapSettings
        {
            public static Dictionary<string, Type> ServerConfigs = new Dictionary<string, Type>()
            {
                {"AllowPublicLocations", typeof(bool)},
                {"SkipPinRange", typeof(float)}
            };

            public static Dictionary<string, ConfigEntryBase> ConfigEntries =
                new Dictionary<string, ConfigEntryBase>();

            public static ConfigEntry<KeyCode> SyncMapKey { get; private set; }
            public static ConfigEntry<KeyCode> SyncPinKey { get; private set; }
            public static ConfigEntry<KeyCode> AcceptMapKey { get; private set; }
            public static ConfigEntry<KeyCode> RejectMapKey { get; private set; }
            public static ConfigEntry<float> SkipPinRange { get; private set; }

            public static ConfigEntry<bool> AcceptPinShares { get; private set; }

            public static ConfigEntry<bool> SendPinShares { get; private set; }

            public static ConfigEntry<bool> AllowPublicLocations { get; private set; }

            public static void Init(ConfigFile config)
            {
                string name = "MapSettings";
                SyncMapKey = config.Bind(name, "SyncMapKey", KeyCode.F9,
                    "What key to press to send map to target?");
                ConfigEntries.Add(nameof(SyncMapKey), SyncMapKey);

                SyncPinKey = config.Bind(name, "SyncPinKey", KeyCode.F10,
                    "What key to press to send pins only to target?");
                ConfigEntries.Add(nameof(SyncPinKey), SyncPinKey);

                AcceptMapKey = config.Bind(name, "AcceptMapKey", KeyCode.F7,
                    "What key to press to accept a sent map?");
                ConfigEntries.Add(nameof(AcceptMapKey), AcceptMapKey);

                RejectMapKey = config.Bind(name, "RejectMapKey", KeyCode.F8,
                    "What key to press to reject a sent map?");
                ConfigEntries.Add(nameof(RejectMapKey), RejectMapKey);

                SkipPinRange = config.Bind(name, "SkipPinRange", 25f,
                    "How close are pins before the incoming one is ignored during a merge?");
                ConfigEntries.Add(nameof(SkipPinRange), SkipPinRange);

                AcceptPinShares = config.Bind(name, "AcceptPinShares", true,
                    "Accept pins that are shared along with exploration data. Defaults to true.");
                ConfigEntries.Add(nameof(AcceptPinShares), AcceptPinShares);

                SendPinShares = config.Bind(name, "SendPinShares", true,
                    "Send other players your pins along with map data? Defaults to true.");
                ConfigEntries.Add(nameof(SendPinShares), SendPinShares);

                AllowPublicLocations = config.Bind(name, "AllowPublicLocations", true,
                    "Allow players to enable their public locations?");
                ConfigEntries.Add(nameof(AllowPublicLocations), AllowPublicLocations);

                Utils.Log(
                    $"Loaded settings!\nSyncMapKey: {SyncMapKey.Value}\nAcceptMapKey:{AcceptMapKey.Value}\nRejectMapKey:{RejectMapKey.Value}\nSkipPinRange:{SkipPinRange.Value}");
            }
        }
    }
}