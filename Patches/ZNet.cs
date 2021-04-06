using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MapSharingMadeEasy.Patches
{
    [HarmonyPatch(typeof(ZNet))]
    [HarmonyPriority(2147483647)]
    public static class ZNet_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("Awake")]
        public static void Postfix(ZNet __instance)
        {
            Utils.Log("Patching Znet for configs");
            RegisterNewNetRPCs(__instance.m_routedRpc);
        }

        [HarmonyPostfix]
        [HarmonyPatch("RPC_CharacterID")]
        public static void Postfix(ZNet __instance, ZRpc rpc, ZDOID characterID)
        {
            if (!__instance.IsDedicated() && !__instance.IsServer())
                return;
            
            SendModVersionToClient(__instance.GetPeer(rpc).m_uid, MapSharingMadeEasy.instance.PluginVersion);
            SendConfigToClient(__instance.GetPeer(rpc).m_uid);
        }

        private static void SendModVersionToClient(long peerID, string instancePluginVersion)
        {
            if (!ZNet.instance.IsDedicated() || !ZNet.instance.IsServer())
                return;
            
            ZPackage zpg = new ZPackage();
            var version = new object[] {instancePluginVersion};
            ZRpc.Serialize(version, ref zpg);
            zpg.SetPos(0);
            Utils.Log($"Sending mod version {version} to client to be checked.");
            ZNet.instance.m_routedRpc.InvokeRoutedRPC(peerID, "CheckMapSharingModVersion", (object) zpg);
        }

        public static void RegisterNewNetRPCs(ZRoutedRpc zrpc)
        {
            Utils.Log($"Registering server side and client side RPCs for Map Sharing Made Easy");
            if (zrpc == null)
            {
                Utils.Log("No zrpc instance found");
                return;
            }
            
            zrpc.Register("SetMapSharingConfigValues", new Action<long, ZPackage>(RPC_ClientSetConfigValues));
            zrpc.Register("CheckMapSharingModVersion", new Action<long, ZPackage>(RPC_ClientCheckModVersion));
            zrpc.Register("ReceiveMapData", new Action<long, ZPackage>(MapTransfer.RPC_ReceiveMapData));
        }

        private static void RPC_ClientCheckModVersion(long sender, ZPackage zpkg)
        {
            if (ZNet.instance == null)
            {
                Utils.Log("No ZNet instance found.");
                return;
            }

            if (ZNet.instance.IsDedicated() || ZNet.instance.IsServer())
            {
                Utils.Log("This is server. Don't run Client CheckModVersion");
                return;
            }

            var serverVersion = (string)zpkg.ReadVariable(typeof(string));
            Utils.Log($"Checking Map Sharing Made Easy Version: Server: {serverVersion} client {MapSharingMadeEasy.instance.PluginVersion}");
            if (serverVersion != MapSharingMadeEasy.instance.PluginVersion)
            {
                Utils.Log($"Wrong mod version detected for Map Sharing Made Easy: Server: {serverVersion} client {MapSharingMadeEasy.instance.PluginVersion}");
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"You must be running {serverVersion} of Map Sharing Made Easy to connect to this server.");
                Game.instance.Logout();
            }
        }

        private static void RPC_ClientSetConfigValues(long sender, ZPackage zpkg)
        {
            if (ZNet.instance.IsDedicated() || ZNet.instance.IsServer())
                return;

            var configSettings = Settings.MapSettings.ServerConfigs;
            foreach (var configSetting in configSettings)
            {
                var config = Settings.MapSettings.ConfigEntries[configSetting.Key];
                config.BoxedValue = zpkg.ReadVariable(configSetting.Value);
                Utils.Log($"Server forced config: {configSetting.Key}: {config.BoxedValue}");
            }
        }

        public static object ReadVariable(this ZPackage zp, Type t)
        {
            if (t == typeof(int))
                return zp.ReadInt();
            if (t == typeof(uint))
                return zp.ReadUInt();
            if (t == typeof(bool))
                return zp.ReadBool();
            if (t == typeof(char))
                return zp.ReadChar();
            if (t == typeof(sbyte))
                return zp.ReadSByte();
            if (t == typeof(long))
                return zp.ReadLong();
            if (t == typeof(ulong))
                return zp.ReadULong();
            if (t == typeof(float))
                return zp.ReadSingle();
            if (t == typeof(double))
                return zp.ReadDouble();
            if (t == typeof(string))
                return zp.ReadString();

            return null;
        }
        
        private static void SendConfigToClient(long peerID)
        {
            if (!ZNet.instance.IsDedicated() || !ZNet.instance.IsServer())
                return;
            Utils.Log("Sending server side configs to client.");
            ZPackage zpg = new ZPackage();
            var configSettings = Settings.MapSettings.ServerConfigs;
            var settings = new List<object>();
            foreach (var configSetting in configSettings)
            {
                Utils.Log($"Forcing server config on client: {configSetting.Key}: {Settings.MapSettings.ConfigEntries[configSetting.Key].BoxedValue}");
                var settingsValue = Settings.MapSettings.ConfigEntries[configSetting.Key].BoxedValue;
                settings.Add(settingsValue);
            }

            ZRpc.Serialize(settings.ToArray(), ref zpg);
            zpg.SetPos(0);
            ZNet.instance.m_routedRpc.InvokeRoutedRPC(peerID, "SetMapSharingConfigValues", (object) zpg);
        }
    }
}