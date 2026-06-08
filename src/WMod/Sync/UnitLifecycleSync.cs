using System;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using WMod.Net;

namespace WMod.Sync;

// Plan B-B Step 3: host announces unit spawns and deaths so the client
// (whose own sim is paused) can mirror the population. Without this, the
// client keeps every unit it had from the initial snapshot forever — dead
// units linger and new spawns never appear.
internal static class UnitLifecycleSync
{
    // Guard so applying a remote spawn/death doesn't re-broadcast.
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

    public static void BroadcastSpawn(Actor actor)
    {
        if (_applyingRemote) return;
        if (actor == null || (NetworkManager.Role & NetRole.Host) == 0) return;
        var tile = actor.current_tile;
        var payload = new SpawnPayload
        {
            id = actor.id,
            assetId = actor.asset != null ? actor.asset.id : "human",
            tileX = tile != null ? tile.x : (int)actor.current_position.x,
            tileY = tile != null ? tile.y : (int)actor.current_position.y,
        };
        NetworkManager.Send(NetMessage.Create("UNIT_SPAWN", JsonConvert.SerializeObject(payload)));
    }

    public static void BroadcastDeath(Actor actor)
    {
        if (_applyingRemote) return;
        if (actor == null || (NetworkManager.Role & NetRole.Host) == 0) return;
        var payload = new DeathPayload { id = actor.id };
        NetworkManager.Send(NetMessage.Create("UNIT_DEATH", JsonConvert.SerializeObject(payload)));
    }

    public static void HandleRemoteSpawn(string body)
    {
        try
        {
            var p = JsonConvert.DeserializeObject<SpawnPayload>(body);
            if (p == null) return;
            var map = MapBox.instance;
            if (map == null || map.units == null) return;
            var tile = map.GetTile(p.tileX, p.tileY);
            if (tile == null) return;
            using (Scope())
            {
                var actor = map.units.spawnNewUnit(
                    pActorAssetID: p.assetId,
                    pTile: tile,
                    pSpawnSound: false,
                    pMiracleSpawn: false,
                    pSpawnHeight: 0f,
                    pSubspecies: null,
                    pGiveOwnerlessItems: false,
                    pAdultAge: true);
                if (actor != null && actor.data != null) actor.data.id = p.id;
            }
        }
        catch (Exception ex) { Debug.Log($"[WMod] HandleRemoteSpawn error: {ex.Message}"); }
    }

    public static int DeathsApplied;
    public static int DeathsNotFound;

    public static void HandleRemoteDeath(string body)
    {
        try
        {
            var p = JsonConvert.DeserializeObject<DeathPayload>(body);
            if (p == null) return;
            var map = MapBox.instance;
            if (map == null || map.units == null) return;
            Actor target = null;
            var alive = map.units.units_only_alive;
            for (int i = 0; i < alive.Count; i++)
            {
                if (alive[i] != null && alive[i].id == p.id) { target = alive[i]; break; }
            }
            if (target == null) { DeathsNotFound++; return; }
            using (Scope())
            {
                try { target.dieSimpleNone(); }
                catch
                {
                    // dieSimpleNone may also throw with the sim paused — at least
                    // pull the actor out of the alive list so it stops rendering.
                    map.units.units_only_alive.Remove(target);
                }
                DeathsApplied++;
            }
            if ((DeathsApplied + DeathsNotFound) % 20 == 0)
            {
                Debug.Log($"[WMod] death apply stats: applied={DeathsApplied} not_found={DeathsNotFound}");
            }
        }
        catch (Exception ex) { Debug.Log($"[WMod] HandleRemoteDeath error: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(ActorManager), nameof(ActorManager.spawnNewUnit))]
public static class ActorSpawnPatch
{
    [HarmonyPostfix]
    public static void Postfix(Actor __result)
    {
        if (__result == null) return;
        UnitLifecycleSync.BroadcastSpawn(__result);
    }
}

[HarmonyPatch(typeof(Actor), nameof(Actor.die))]
public static class ActorDiePatch
{
    [HarmonyPostfix]
    public static void Postfix(Actor __instance)
    {
        if (__instance == null) return;
        UnitLifecycleSync.BroadcastDeath(__instance);
    }
}
