using System;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using WMod.Net;
using WMod.Rules;

namespace WMod.Sync;

internal class WorldGenPayload
{
    public long seed { get; set; }
    public string mapSize { get; set; }
    public string mapTemplate { get; set; }
}

internal static class WorldGenSync
{
    // When non-zero, Randy.fullReset/clearWorld will adopt this seed instead of
    // DateTime.Now / current_world_seed_id+1. Cleared once consumed (single-use).
    public static long PendingSeed;

    // Same value mirrored for the in-gen seed re-roll inside b__157_4. Lives
    // longer than PendingSeed because the re-roll happens later in the pipeline.
    public static int PendingGenSeed;

    public static void HostStartNewWorld()
    {
        var seed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var size = Config.customMapSize;
        var template = Config.current_map_template;

        PendingSeed = seed;
        PendingGenSeed = unchecked((int)seed);
        BroadcastGen(seed, size, template);
        WModBridge.Toast($"[G] world gen broadcast seed={seed} size={size}");
        Debug.Log($"[WMod] WorldGen: host seed={seed} size={size} tpl={template}");

        TriggerLocalGenerate();
    }

    public static void HandleRemoteGen(string payload)
    {
        try
        {
            var p = JsonConvert.DeserializeObject<WorldGenPayload>(payload);
            if (p == null) return;
            if (!string.IsNullOrEmpty(p.mapSize)) Config.customMapSize = p.mapSize;
            if (!string.IsNullOrEmpty(p.mapTemplate)) Config.current_map_template = p.mapTemplate;
            PendingSeed = p.seed;
            PendingGenSeed = unchecked((int)p.seed);
            WModBridge.Toast($"[WORLD_GEN<-] generating seed={p.seed} size={p.mapSize}");
            Debug.Log($"[WMod] WorldGen: client received seed={p.seed} size={p.mapSize}");
            TriggerLocalGenerate();
        }
        catch (Exception ex)
        {
            Debug.Log($"[WMod] HandleRemoteGen error: {ex}");
            WModBridge.Toast($"[WORLD_GEN<-] error: {ex.Message}");
        }
    }

    private static void BroadcastGen(long seed, string size, string template)
    {
        var payload = JsonConvert.SerializeObject(new WorldGenPayload
        {
            seed = seed,
            mapSize = size,
            mapTemplate = template,
        });
        NetworkManager.Send(NetMessage.Create("WORLD_GEN", payload));
    }

    private static void TriggerLocalGenerate()
    {
        // Schedule a normal "click generate" — matches what the player would do from the menu.
        // Runs on next Update because we may be called from a network thread.
        var map = MapBox.instance;
        if (map == null) { Debug.Log("[WMod] WorldGen: MapBox null, cannot generate"); return; }
        Config.load_new_map = true;
        try { map.clickGenerateNewMap(); }
        catch (Exception ex) { Debug.Log($"[WMod] WorldGen trigger error: {ex}"); }
    }
}

[HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
public static class ClearWorldPatch
{
    // clearWorld increments MapBox.current_world_seed_id by 1 at its start.
    // We overwrite it post-hoc with the synced seed so both peers feed the same
    // value into the rest of the generation pipeline.
    [HarmonyPostfix]
    public static void Postfix()
    {
        var pending = WorldGenSync.PendingSeed;
        if (pending == 0) return;
        WorldGenSync.PendingSeed = 0;

        var seed32 = unchecked((int)pending);
        MapBox.current_world_seed_id = seed32;
        Randy._seed = pending;
        Randy.resetSeed(pending);
        Debug.Log($"[WMod] clearWorld seed override -> current_world_seed_id={seed32} Randy._seed={pending}");
    }
}

// Kept for Phase 2 (lockstep). Currently dormant — only fires when PendingSeed != 0
// AND fullReset is invoked while we have one queued. Safe to leave installed.
[HarmonyPatch(typeof(Randy), nameof(Randy.fullReset))]
public static class RandyFullResetPatch
{
    [HarmonyPrefix]
    public static bool Prefix()
    {
        var pending = WorldGenSync.PendingSeed;
        if (pending == 0) return true;
        WorldGenSync.PendingSeed = 0;
        Randy._seed = pending;
        Randy.resetSeed(pending);
        Debug.Log($"[WMod] Randy.fullReset overridden with synced seed={pending}");
        return false;
    }
}

// generateNewMap contains a lambda b__157_4 that re-rolls Randy with
// Randy.resetSeed(Randy.randomInt(1, 555555555)). The intermediate randomInt
// can drift if any earlier gen stage consumed RNG differently across peers.
// We patch Randy.resetSeed(int) directly to substitute the synced seed for the
// FIRST call after a gen request; subsequent resetSeed calls flow through.
[HarmonyPatch(typeof(Randy), nameof(Randy.resetSeed), new System.Type[] { typeof(int) })]
public static class RandyResetSeedIntPatch
{
    [HarmonyPrefix]
    public static void Prefix(ref int pIntValue)
    {
        if (WorldGenSync.PendingGenSeed == 0) return;
        var s = WorldGenSync.PendingGenSeed;
        WorldGenSync.PendingGenSeed = 0;
        pIntValue = s;
        Debug.Log($"[WMod] Randy.resetSeed(int) intercepted -> {s}");
    }
}
