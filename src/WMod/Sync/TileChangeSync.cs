using System;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using WMod.Net;

namespace WMod.Sync;

// Plan B-B Step 4: mirror terrain changes from the host to the paused client.
// Patches WorldTile.setTileType / setTileTypes Postfix on the host, packages
// the (x, y, mainType, topType) change, and broadcasts it. The client replays
// it via the same setter on its WorldTile, looked up by coords.
internal static class TileChangeSync
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

    internal class Payload
    {
        public int x;
        public int y;
        public string main;
        public string top;
    }

    public static void BroadcastChange(WorldTile tile, TileType main, TopTileType top)
    {
        if (_applyingRemote) return;
        if (tile == null || (NetworkManager.Role & NetRole.Host) == 0) return;
        var p = new Payload
        {
            x = tile.x,
            y = tile.y,
            main = main?.id,
            top = top?.id,
        };
        NetworkManager.Send(NetMessage.Create("TILE_CHANGE", JsonConvert.SerializeObject(p)));
    }

    public static int Applied;
    public static int Skipped;

    public static void HandleRemote(string body)
    {
        try
        {
            var p = JsonConvert.DeserializeObject<Payload>(body);
            if (p == null) return;
            var map = MapBox.instance;
            if (map == null) return;
            var tile = map.GetTile(p.x, p.y);
            if (tile == null) { Skipped++; return; }

            TileType main = !string.IsNullOrEmpty(p.main) ? AssetManager.tiles.get(p.main) : null;
            TopTileType top = !string.IsNullOrEmpty(p.top) ? AssetManager.top_tiles.get(p.top) : null;

            using (Scope())
            {
                if (main != null && top != null) tile.setTileTypes(main, top, true);
                else if (main != null) tile.setTileType(main, true);
            }
            Applied++;
            if ((Applied + Skipped) % 50 == 0) Debug.Log($"[WMod] TILE_CHANGE stats applied={Applied} skipped={Skipped}");
        }
        catch (Exception ex) { Debug.Log($"[WMod] HandleRemote(TILE_CHANGE) error: {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(WorldTile), nameof(WorldTile.setTileType), new System.Type[] { typeof(TileType), typeof(bool) })]
public static class TileSetTypeSinglePatch
{
    [HarmonyPostfix]
    public static void Postfix(WorldTile __instance, TileType pType)
    {
        TileChangeSync.BroadcastChange(__instance, pType, null);
    }
}

[HarmonyPatch(typeof(WorldTile), nameof(WorldTile.setTileTypes), new System.Type[] { typeof(TileType), typeof(TopTileType), typeof(bool) })]
public static class TileSetTypesBothPatch
{
    [HarmonyPostfix]
    public static void Postfix(WorldTile __instance, TileType pType, TopTileType pTopTile)
    {
        TileChangeSync.BroadcastChange(__instance, pType, pTopTile);
    }
}
