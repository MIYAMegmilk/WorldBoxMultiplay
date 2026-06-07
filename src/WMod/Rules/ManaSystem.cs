using System.Collections.Generic;
using UnityEngine;

namespace WMod.Rules;

internal class PlayerMana
{
    public float current = 100f;
    public float max = 100f;
    public float regenPerSec = 5f;
    public readonly Dictionary<string, float> cooldownExpiresAt = new Dictionary<string, float>();
}

internal static class ManaSystem
{
    private const float DefaultCost = 10f;
    private const float DefaultCooldownSec = 0.5f;

    private static readonly Dictionary<int, PlayerMana> _state = new Dictionary<int, PlayerMana>();

    public static PlayerMana Get(int playerId)
    {
        if (playerId < 0) return null;
        if (!_state.TryGetValue(playerId, out var p))
        {
            p = new PlayerMana();
            _state[playerId] = p;
        }
        return p;
    }

    public static float CostOf(GodPower power) => DefaultCost;
    public static float CooldownOf(GodPower power) => DefaultCooldownSec;

    public static bool CanUse(int playerId, GodPower power, out string reason)
    {
        reason = null;
        var p = Get(playerId);
        if (p == null) { reason = "no mana pool"; return false; }
        var cost = CostOf(power);
        if (p.current < cost) { reason = $"mana {p.current:0}/{cost:0}"; return false; }
        if (p.cooldownExpiresAt.TryGetValue(power.id, out var until) && Time.unscaledTime < until)
        {
            reason = $"cooldown {(until - Time.unscaledTime):0.0}s";
            return false;
        }
        return true;
    }

    public static void Consume(int playerId, GodPower power)
    {
        var p = Get(playerId);
        if (p == null) return;
        p.current -= CostOf(power);
        if (p.current < 0) p.current = 0;
        p.cooldownExpiresAt[power.id] = Time.unscaledTime + CooldownOf(power);
    }

    public static void Tick(float dt)
    {
        foreach (var p in _state.Values)
        {
            if (p.current < p.max)
            {
                p.current += p.regenPerSec * dt;
                if (p.current > p.max) p.current = p.max;
            }
        }
    }

    public static void Reset() => _state.Clear();
}
