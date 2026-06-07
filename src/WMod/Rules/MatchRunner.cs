using Newtonsoft.Json;
using UnityEngine;
using WMod.Net;

namespace WMod.Rules;

internal static class MatchRunner
{
    public static IWinCondition Rule { get; set; } = new LastKingdomStanding();
    private const float CheckIntervalSec = 5f;

    private static float _nextCheckAt;
    private static bool _ended;

    public static void Tick(float now)
    {
        if (_ended) return;
        if ((NetworkManager.Role & NetRole.Host) == 0) return;
        if (now < _nextCheckAt) return;
        _nextCheckAt = now + CheckIntervalSec;

        var result = Rule?.Evaluate();
        if (result == null) return;

        _ended = true;
        var json = JsonConvert.SerializeObject(result);
        NetworkManager.Send(NetMessage.Create("MATCH_RESULT", json));
        Announce(result);
    }

    public static void HandleRemote(string payload)
    {
        try
        {
            var r = JsonConvert.DeserializeObject<MatchResult>(payload);
            if (r == null) return;
            _ended = true;
            Announce(r);
        }
        catch { }
    }

    public static void Reset()
    {
        _ended = false;
        _nextCheckAt = 0;
    }

    private static void Announce(MatchResult r)
    {
        string who;
        if (r.winnerId < 0) who = "DRAW";
        else if (r.winnerId == PlayerRegistry.Self.id) who = "YOU WIN!";
        else if (PlayerRegistry.Peers.TryGetValue(r.winnerId, out var p))
            who = $"WINNER: {p.name} (id={p.id})";
        else who = $"WINNER: id={r.winnerId}";

        WModBridge.Toast($"=== MATCH OVER === {who} — {r.reason}");
        Debug.Log($"[WMod][MATCH] {who} | {r.reason}");
    }
}
