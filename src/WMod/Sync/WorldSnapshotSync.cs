using System;
using UnityEngine;
using WMod.Net;

namespace WMod.Sync;

internal static class WorldSnapshotSync
{
    public static float AutoIntervalSec; // 0 = off
    private static float _nextAutoAt;
    private static readonly float[] _intervalCycle = new float[] { 0f, 5f, 10f, 30f };

    public static void CycleAutoInterval()
    {
        int idx = System.Array.IndexOf(_intervalCycle, AutoIntervalSec);
        if (idx < 0) idx = 0;
        idx = (idx + 1) % _intervalCycle.Length;
        AutoIntervalSec = _intervalCycle[idx];
        _nextAutoAt = AutoIntervalSec > 0
            ? UnityEngine.Time.unscaledTime + AutoIntervalSec
            : 0f;
        WModBridge.Toast(AutoIntervalSec == 0
            ? "[Numpad +] auto-snapshot OFF"
            : $"[Numpad +] auto-snapshot every {AutoIntervalSec:0}s");
    }

    public static void TickAuto(float now)
    {
        if (AutoIntervalSec <= 0) return;
        if ((NetworkManager.Role & NetRole.Host) == 0) return;
        if (now < _nextAutoAt) return;
        _nextAutoAt = now + AutoIntervalSec;
        HostSendSnapshot();
    }

    public static string StatusLine()
    {
        if (AutoIntervalSec <= 0) return "auto OFF";
        var remain = System.Math.Max(0, _nextAutoAt - UnityEngine.Time.unscaledTime);
        return $"auto {AutoIntervalSec:0}s (next in {remain:0.0}s)";
    }

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
