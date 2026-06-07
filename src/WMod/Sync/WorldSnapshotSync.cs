using System;
using UnityEngine;
using WMod.Net;

namespace WMod.Sync;

internal static class WorldSnapshotSync
{
    public static void HostSendSnapshot()
    {
        try
        {
            var map = MapBox.instance;
            if (map == null) { WModBridge.Toast("snapshot: MapBox not ready"); return; }

            var saved = SaveManager.currentWorldToSavedMap();
            var bytes = saved.toZip();
            var b64 = Convert.ToBase64String(bytes);
            NetworkManager.Send(NetMessage.Create("WORLD_SNAPSHOT", b64));

            WModBridge.Toast($"[snapshot] sent {bytes.Length:N0} bytes ({b64.Length:N0} b64)");
            Debug.Log($"[WMod] snapshot sent: {bytes.Length} raw / {b64.Length} b64");
        }
        catch (Exception ex)
        {
            WModBridge.Toast($"snapshot send error: {ex.Message}");
            Debug.Log($"[WMod] HostSendSnapshot error: {ex}");
        }
    }

    public static void HandleRemoteSnapshot(string payload)
    {
        try
        {
            var bytes = Convert.FromBase64String(payload);
            WModBridge.Toast($"[snapshot<-] loading {bytes.Length:N0} bytes");
            Debug.Log($"[WMod] applying remote snapshot: {bytes.Length} bytes");
            SaveManager.loadMapFromBytes(bytes);
        }
        catch (Exception ex)
        {
            WModBridge.Toast($"snapshot load error: {ex.Message}");
            Debug.Log($"[WMod] HandleRemoteSnapshot error: {ex}");
        }
    }
}
