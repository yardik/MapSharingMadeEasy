using MapSharingMadeEasy.Patches;
using UnityEngine;

namespace MapSharingMadeEasy
{
    public class MapTransfer : MonoBehaviour
    {
        public static void SendMapDataToClient(long peerId, string mapData)
        {
            ZPackage zpg = new ZPackage();
            var mapdataArray = new object[] {mapData};
            zpg.Write(mapData);
            zpg.SetPos(0);
            Utils.Log($"Sending mapdata size: {mapData.Length} to client {peerId}.");
            ZNet.instance.m_routedRpc.InvokeRoutedRPC(peerId, "ReceiveMapData", (object) zpg);
        }

        public static void RPC_ReceiveMapData(long peerID, ZPackage zpkg)
        {
            if (ZNet.instance.IsServer())
            {
                Utils.Log("Server RPC_ReceiveMapData");
                ServerTransmitMapData(zpkg);
                return;
            }
            
            var text = zpkg.ReadString();
            Utils.Log($"MapData received! Size: {text.Length}");
            var validData = MapData.ParseReceivedMapData(text, out var sentFrom, out var sentTo, out var pluginVersion,
                out var pins, out var mapData);
            if (validData)
            {
                Utils.Log($"Received map data from {sentFrom} via chat.");
                MapData.instance.SyncData = text;
                MapData.instance.MapSender = sentFrom;
            }
            else
            {
                Utils.Log("MapData was invalid, ignoring.");
            }
        }

        private static void ServerTransmitMapData(ZPackage zpkg)
        {
            var rString = zpkg.ReadString();
            zpkg.SetPos(0);
            var validData = MapData.ParseReceivedMapData(rString, out var sentFrom, out var sentTo, out var pluginVersion,
                out var pins, out var mapData);
            if (validData)
            {
                Debug.Log($"Valid map data found retransmitting...");
                var peer = ZNet.instance.GetPeerByPlayerName(sentTo);
                if (peer != null)
                {
                    Utils.Log($"Retransmitting Map Data to {peer.m_playerName}");
                    SendMapDataToClient(peer.m_uid, rString);
                }
            }
        }
    }
}