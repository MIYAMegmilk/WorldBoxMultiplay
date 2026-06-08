using System;
using System.IO;
using System.Text;
using UnityEngine;
using WMod.Net;
using WMod.Rules;

namespace WMod.Sync;

// Lets the test runner exercise the mod end-to-end without a human in the
// loop. Driven by env vars set before WorldBox launches:
//   WMOD_AUTOTEST = 1                       enable auto mode
//   WMOD_ROLE     = host | client           role the peer should take
//   WMOD_HOSTADDR = 127.0.0.1               client's connect target (default loopback)
//   WMOD_DUMP_DIR = C:\Temp\wmod_autotest   directory for periodic state dumps
//   WMOD_DUMP_INTERVAL = 2.0                seconds between dumps
internal static class AutoTest
{
    public enum State { Off, WaitingForMapBox, WaitingForWorld, ReadyToConnect, Connected, Running }

    public static State CurrentState = State.Off;
    public static string Role;
    public static string HostAddr = "127.0.0.1";
    public static string DumpDir;
    public static float DumpInterval = 2f;
    public static string InitialSave;
    private static float _nextDumpAt;
    private static float _waitStartedAt;
    private static int _dumpCounter;

    public static void Init()
    {
        var enable = Environment.GetEnvironmentVariable("WMOD_AUTOTEST");
        if (string.IsNullOrEmpty(enable) || enable == "0") return;

        Role = (Environment.GetEnvironmentVariable("WMOD_ROLE") ?? "").ToLowerInvariant();
        if (Role != "host" && Role != "client")
        {
            Debug.Log($"[WMod][AutoTest] WMOD_ROLE must be host or client, got '{Role}' — disabled");
            return;
        }

        HostAddr = Environment.GetEnvironmentVariable("WMOD_HOSTADDR") ?? "127.0.0.1";
        DumpDir = Environment.GetEnvironmentVariable("WMOD_DUMP_DIR") ?? Path.Combine(Path.GetTempPath(), "wmod_autotest");
        InitialSave = Environment.GetEnvironmentVariable("WMOD_INITIAL_SAVE");
        var iv = Environment.GetEnvironmentVariable("WMOD_DUMP_INTERVAL");
        if (!string.IsNullOrEmpty(iv)) float.TryParse(iv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out DumpInterval);

        try { Directory.CreateDirectory(DumpDir); } catch { }

        CurrentState = State.WaitingForMapBox;
        _waitStartedAt = Time.unscaledTime;
        Debug.Log($"[WMod][AutoTest] enabled role={Role} hostAddr={HostAddr} dumpDir={DumpDir} interval={DumpInterval}s");
    }

    public static void Tick(float now)
    {
        if (CurrentState == State.Off) return;

        switch (CurrentState)
        {
            case State.WaitingForMapBox:
                if (MapBox.instance != null)
                {
                    if (!string.IsNullOrEmpty(InitialSave) && File.Exists(InitialSave))
                    {
                        Debug.Log($"[WMod][AutoTest] loading initial save from {InitialSave}");
                        try
                        {
                            var bytes = File.ReadAllBytes(InitialSave);
                            SaveManager.loadMapFromBytes(bytes);
                        }
                        catch (Exception ex) { Debug.Log($"[WMod][AutoTest] save load error: {ex.Message}"); }
                    }
                    else
                    {
                        Debug.Log("[WMod][AutoTest] no WMOD_INITIAL_SAVE -> triggering clickGenerateNewMap");
                        try { MapBox.instance.clickGenerateNewMap(); }
                        catch (Exception ex) { Debug.Log($"[WMod][AutoTest] clickGenerateNewMap error: {ex.Message}"); }
                    }
                    CurrentState = State.WaitingForWorld;
                    _waitStartedAt = now;
                }
                else if (now - _waitStartedAt > 60f) Fail("MapBox never appeared");
                break;

            case State.WaitingForWorld:
                // Wait until kingdom manager and units list are valid — a fair proxy for "world is up"
                if (MapBox.instance != null && MapBox.instance.units != null && MapBox.instance.units.units_only_alive != null)
                {
                    if (now - _waitStartedAt < 5f) return; // grace period for the loader to settle
                    var ac = MapBox.instance.units.units_only_alive.Count;
                    Debug.Log($"[WMod][AutoTest] world loaded after {now - _waitStartedAt:F1}s, units_alive={ac} -> ReadyToConnect");
                    CurrentState = State.ReadyToConnect;
                }
                else if (now - _waitStartedAt > 120f) Fail("world never finished loading");
                break;

            case State.ReadyToConnect:
                try
                {
                    if (Role == "host")
                    {
                        NetworkManager.StartHost();
                        PlayerRegistry.SetSelf(0, "Host");
                        Debug.Log("[WMod][AutoTest] hosting on :7777");
                    }
                    else
                    {
                        NetworkManager.ConnectClient(HostAddr);
                        PlayerRegistry.SetSelf(1, "Client");
                        ClientSimMode.Enable();
                        Debug.Log($"[WMod][AutoTest] connected to {HostAddr}:7777, sim paused");
                    }
                    CurrentState = State.Connected;
                    _waitStartedAt = now;
                    // For the host, push an initial snapshot once the client has time to attach
                    if (Role == "host") _nextDumpAt = now + 5f;
                    else _nextDumpAt = now + DumpInterval;
                }
                catch (Exception ex) { Fail($"connect failed: {ex.Message}"); }
                break;

            case State.Connected:
                // Host sends the initial snapshot ~5s after coming up to give the client time to connect
                if (Role == "host" && now - _waitStartedAt > 5f)
                {
                    Debug.Log("[WMod][AutoTest] host pushing initial snapshot");
                    try { WorldSnapshotSync.HostSendSnapshot(); }
                    catch (Exception ex) { Debug.Log($"[WMod][AutoTest] snapshot error: {ex.Message}"); }
                    CurrentState = State.Running;
                    _nextDumpAt = now + DumpInterval;
                }
                else if (Role == "client" && now - _waitStartedAt > 10f)
                {
                    CurrentState = State.Running;
                }
                break;

            case State.Running:
                if (now >= _nextDumpAt)
                {
                    _nextDumpAt = now + DumpInterval;
                    WriteDump();
                }
                break;
        }
    }

    private static void Fail(string reason)
    {
        Debug.Log($"[WMod][AutoTest] FAIL: {reason}");
        CurrentState = State.Off;
    }

    // Drop a small cluster of humans on the host so the autotest has units to
    // measure. The actual content doesn't matter — we just need things that
    // move so position sync has something to track.
    private static void SeedTestUnits()
    {
        var map = MapBox.instance;
        if (map == null || map.units == null) return;
        // ActorManager.spawnNewUnit bypasses GodPower (no player kingdom needed).
        // Use a fixed center near the map's middle so spawn coords are stable
        // across runs.
        int cx = 128, cy = 128;
        int spawned = 0;
        for (int dy = -4; dy <= 4; dy += 2)
        for (int dx = -4; dx <= 4; dx += 2)
        {
            var t = map.GetTile(cx + dx, cy + dy);
            if (t == null) continue;
            try
            {
                var actor = map.units.spawnNewUnit(
                    pActorAssetID: "human",
                    pTile: t,
                    pSpawnSound: false,
                    pMiracleSpawn: false,
                    pSpawnHeight: 0f,
                    pSubspecies: null,
                    pGiveOwnerlessItems: false,
                    pAdultAge: true);
                if (actor != null) spawned++;
            }
            catch (System.Exception ex) { Debug.Log($"[WMod][AutoTest] spawn err at ({cx + dx},{cy + dy}): {ex.Message}"); }
        }
        Debug.Log($"[WMod][AutoTest] seeded {spawned} humans around ({cx},{cy})");
    }

    private static void WriteDump()
    {
        try
        {
            var map = MapBox.instance;
            if (map == null || map.units == null) return;
            var alive = map.units.units_only_alive;
            if (alive == null) return;

            _dumpCounter++;
            var sb = new StringBuilder();
            sb.Append("# WMod autotest dump\n");
            sb.Append("role=").Append(Role).Append('\n');
            sb.Append("seq=").Append(_dumpCounter).Append('\n');
            sb.Append("time_unscaled=").Append(Time.unscaledTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("world_time=").Append(map.map_stats != null ? map.map_stats.history_current_year.ToString() : "?").Append('\n');
            sb.Append("alive_count=").Append(alive.Count).Append('\n');
            sb.Append("# id x y\n");

            for (int i = 0; i < alive.Count; i++)
            {
                var a = alive[i];
                if (a == null) continue;
                var p = a.current_position;
                sb.Append(a.id).Append(' ')
                  .Append(p.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
                  .Append(p.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
                  .Append('\n');
            }

            var path = Path.Combine(DumpDir, $"{Role}_{_dumpCounter:D4}.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[WMod][AutoTest] dump #{_dumpCounter} -> {Path.GetFileName(path)} ({alive.Count} units)");
        }
        catch (Exception ex) { Debug.Log($"[WMod][AutoTest] dump error: {ex.Message}"); }
    }
}
