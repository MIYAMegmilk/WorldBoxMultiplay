using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using WMod.Net;

namespace WMod.Sync;

// Plan B-B Step 2: host streams the live position of every Actor at a fixed
// cadence; the client (whose own sim is paused via ClientSimMode) overrides
// each Actor.transform.position with the host's value. No simulation runs on
// the client, so positions only ever move because they were told to.
internal static class UnitPositionSync
{
    public static float IntervalSec = 0.2f;       // 5 Hz default
    public static bool Enabled = true;
    private static float _nextSendAt;

    public static void TickHost(float now)
    {
        if (!Enabled) return;
        if ((NetworkManager.Role & NetRole.Host) == 0) return;
        if (now < _nextSendAt) return;
        _nextSendAt = now + IntervalSec;

        var map = MapBox.instance;
        if (map == null || map.units == null) return;
        var alive = map.units.units_only_alive;
        if (alive == null) return;

        // Compact serialization: "id,x,y;id,x,y;..." — ~25 bytes per unit
        // beats JSON arrays for thousands of entries.
        var sb = new StringBuilder(alive.Count * 24);
        for (int i = 0; i < alive.Count; i++)
        {
            var a = alive[i];
            if (a == null) continue;
            var p = a.current_position;
            if (sb.Length > 0) sb.Append(';');
            sb.Append(a.id).Append(',').Append(p.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(p.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        }
        NetworkManager.Send(NetMessage.Create("UNIT_POS", sb.ToString()));
    }

    public static void HandleRemote(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return;
        var map = MapBox.instance;
        if (map == null || map.units == null) return;

        // Build a one-shot id -> Actor lookup so we don't pay an O(n*m) cost
        var lookup = BuildLookup(map.units.units_only_alive);
        if (lookup == null) return;

        int applied = 0;
        var rows = payload.Split(';');
        foreach (var row in rows)
        {
            var parts = row.Split(',');
            if (parts.Length != 3) continue;
            if (!long.TryParse(parts[0], out var id)) continue;
            if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)) continue;
            if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)) continue;
            if (!lookup.TryGetValue(id, out var actor) || actor == null) continue;
            actor.current_position = new Vector2(x, y);
            applied++;
        }
        if (applied > 0 && UnityEngine.Random.value < 0.1f)
        {
            // Sparse log so we don't spam.
            Debug.Log($"[WMod] UNIT_POS applied {applied}/{rows.Length}");
        }
    }

    private static Dictionary<long, Actor> BuildLookup(List<Actor> alive)
    {
        if (alive == null) return null;
        var d = new Dictionary<long, Actor>(alive.Count);
        for (int i = 0; i < alive.Count; i++)
        {
            var a = alive[i];
            if (a != null) d[a.id] = a;
        }
        return d;
    }
}
