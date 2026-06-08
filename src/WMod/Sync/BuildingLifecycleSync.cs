using System;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using WMod.Net;

namespace WMod.Sync;

// Plan B-B step 5 (buildings half): mirror Building construction and removal
// from the host. Buildings share the BaseSimObject lineage with Actor, so the
// pattern is the same as UnitLifecycleSync.
internal static class BuildingLifecycleSync
{
    [ThreadStatic] private static bool _applyingRemote;
    public static bool IsApplyingRemote => _applyingRemote;

    public static IDisposable Scope()
    {
        _applyingRemote = true;
        return new Releaser();
    }
    private sealed class Releaser : IDisposable
    {
        public void Dispose() => _applyingRemote = false;
    }

    internal class SpawnPayload
    {
        public long id;
        public string assetId;
        public int tileX;
        public int tileY;
    }

    internal class DeathPayload
    {
        public long id;
    }

    public static int SpawnsApplied;
    public static int DestroysApplied;
    public static int DestroysNotFound;

    public static void BroadcastSpawn(Building b)
    {
        if (_applyingRemote) return;
        if (b == null || (NetworkManager.Role & NetRole.Host) == 0) return;
        var tile = b.current_tile;
        var p = new SpawnPayload
        {
            id = b.id,
            assetId = b.asset != null ? b.asset.id : "",
            tileX = tile != null ? tile.x : (int)b.current_position.x,
            tileY = tile != null ? tile.y : (int)b.current_position.y,
        };
        NetworkManager.Send(NetMessage.Create("BUILDING_SPAWN", JsonConvert.SerializeObject(p)));
    }

    public static void BroadcastDestroy(Building b)
    {
        if (_applyingRemote) return;
        if (b == null || (NetworkManager.Role & NetRole.Host) == 0) return;
        var p = new DeathPayload { id = b.id };
        NetworkManager.Send(NetMessage.Create("BUILDING_DESTROY", JsonConvert.SerializeObject(p)));
    }

    public static void HandleRemoteSpawn(string body)
    {
        try
        {
            var p = JsonConvert.DeserializeObject<SpawnPayload>(body);
            if (p == null || string.IsNullOrEmpty(p.assetId)) return;
            var map = MapBox.instance;
            if (map == null) return;
            var tile = map.GetTile(p.tileX, p.tileY);
            if (tile == null) return;
            using (Scope())
            {
                var b = map.buildings.addBuilding(
                    pID: p.assetId,
                    pTile: tile,
                    pCheckForBuild: false,
                    pSfx: false,
                    pType: BuildPlacingType.Load);
                if (b != null && b.data != null) b.data.id = p.id;
            }
            SpawnsApplied++;
        }
        catch (Exception ex) { Debug.Log($"[WMod] HandleRemoteSpawn(BUILDING) error: {ex.Message}"); }
    }

    public static void HandleRemoteDestroy(string body)
    {
        try
        {
            var p = JsonConvert.DeserializeObject<DeathPayload>(body);
            if (p == null) return;
            var map = MapBox.instance;
            if (map == null) return;
            // SystemManager.get(long) is the canonical id -> object lookup
            var target = map.buildings.get(p.id);
            if (target == null) { DestroysNotFound++; return; }
            using (Scope())
            {
                try { target.kill(); } catch { /* best effort under paused sim */ }
            }
            DestroysApplied++;
        }
        catch (Exception ex) { Debug.Log($"[WMod] HandleRemoteDestroy(BUILDING) error: {ex.Message}"); }
    }
}

// Disabled: BuildingManager.addBuilding auto-allocates an id when registering
// the new Building in its dict; trying to overwrite data.id afterwards
// produces an "An item with the same key has already been added" exception
// because the dict already keyed the building under the original id. The
// proper fix is to intercept the id at allocation time, or to keep a
// host-id -> client-id map. Postponed; for now buildings sync only at
// the initial snapshot and via Step 3 actor-style hooks won't apply.
//
// [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.addBuilding), new System.Type[] { typeof(BuildingAsset), typeof(WorldTile), typeof(bool), typeof(bool), typeof(BuildPlacingType) })]
// public static class BuildingAddPatch
// {
//     [HarmonyPostfix]
//     public static void Postfix(Building __result)
//     {
//         if (__result == null) return;
//         BuildingLifecycleSync.BroadcastSpawn(__result);
//     }
// }

// Disabled along with the spawn patch — destroy without spawn is asymmetric
// and risks orphan ids on the client. Re-enable when spawn id collisions
// are resolved.
//
// [HarmonyPatch(typeof(Building), nameof(Building.kill))]
// public static class BuildingKillPatch
// {
//     [HarmonyPostfix]
//     public static void Postfix(Building __instance)
//     {
//         if (__instance == null) return;
//         BuildingLifecycleSync.BroadcastDestroy(__instance);
//     }
// }
