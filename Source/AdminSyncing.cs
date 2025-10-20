using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RPGStatusEffects
{
    public static class AdminSyncing
    {
        private static bool isServer;
        internal static bool registeredOnClient;

        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class AdminStatusSyncPatch
        {
            [HarmonyPostfix]
            [HarmonyPriority(700)]
            private static void Postfix(ZNet __instance)
            {
                isServer = __instance.IsServer();
                if (RPGStatusEffects.Instance == null) return;
                if (isServer)
                {
                    ZRoutedRpc.instance.Register<ZPackage>(RPGStatusEffects.PluginName + " AdminStatusSync", RPC_AdminStatusSync);
                }
                else if (!registeredOnClient)
                {
                    ZRoutedRpc.instance.Register<ZPackage>(RPGStatusEffects.PluginName + " AdminStatusSync", RPC_AdminStatusSync);
                    registeredOnClient = true;
                }
                if (isServer)
                    __instance.StartCoroutine(WatchAdminListChanges());
            }

            private static IEnumerator WatchAdminListChanges()
            {
                var adminList = (SyncedList)AccessTools.Field(typeof(ZNet), "m_adminList").GetValue(ZNet.instance);
                var currentList = new List<string>(adminList.GetList());
                while (true)
                {
                    yield return new WaitForSeconds(30f);
                    var newList = adminList.GetList();
                    if (!newList.SequenceEqual(currentList))
                    {
                        currentList = new List<string>(newList);
                        var peers = ZNet.instance.GetPeers();
                        var adminPeers = peers.Where(p => adminList.Contains(p.m_rpc.GetSocket().GetHostName())).ToList();
                        SendAdmin(peers.Except(adminPeers).ToList(), false);
                        SendAdmin(adminPeers, true);
                    }
                }
            }

            private static void SendAdmin(List<ZNetPeer> peers, bool isAdmin)
            {
                var package = new ZPackage();
                package.Write(isAdmin);
                ZNet.instance.StartCoroutine(SendZPackage(peers, package));
            }
        }

        private static void RPC_AdminStatusSync(long sender, ZPackage package)
        {
            bool isAdmin = false;
            try
            {
                isAdmin = package.ReadBool();
            }
            catch { }
            if (isServer)
            {
                var peer = ZNet.instance.GetPeer(sender);
                var adminList = (SyncedList)AccessTools.Field(typeof(ZNet), "m_adminList").GetValue(ZNet.instance);
                if (peer == null || !adminList.Contains(peer.m_rpc.GetSocket().GetHostName())) return;
                var pkg = new ZPackage();
                pkg.Write(true);
                peer.m_rpc.Invoke(RPGStatusEffects.PluginName + " AdminStatusSync", pkg);
            }
            else
            {
                ConfigSync.lockExempt = isAdmin;
                RPGStatusEffects.Instance?.SetupStatusEffects();
                RPGStatusEffects.Instance?.AddTauntHammer(); // Line 88: Now accessible since AddTauntHammer is public
            }
        }

        internal static IEnumerator SendZPackage(List<ZNetPeer> peers, ZPackage package)
        {
            if (!ZNet.instance) yield break;
            byte[] data = package.GetArray();
            if (data.Length > 10000)
            {
                var compressed = new ZPackage();
                compressed.Write(4);
                using var output = new MemoryStream();
                using (var deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
                    deflate.Write(data, 0, data.Length);
                compressed.Write(output.ToArray());
                package = compressed;
            }
            var writers = peers.Where(p => p.IsReady()).Select(p => TellPeerAdminStatus(p, package)).ToList();
            writers.RemoveAll(w => !w.MoveNext());
            while (writers.Count > 0)
            {
                yield return null;
                writers.RemoveAll(w => !w.MoveNext());
            }
        }

        private static IEnumerator<bool> TellPeerAdminStatus(ZNetPeer peer, ZPackage package)
        {
            if (ZRoutedRpc.instance != null)
            {
                var pkg = new ZPackage(package.GetArray());
                if (isServer)
                    peer.m_rpc.Invoke(RPGStatusEffects.PluginName + " AdminStatusSync", pkg);
                else
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_server ? 0L : peer.m_uid, RPGStatusEffects.PluginName + " AdminStatusSync", pkg);
            }
            yield return false;
        }
    }
}