using System.Collections.Generic;

namespace WMod.Rules;

internal class LastKingdomStanding : IWinCondition
{
    public string Name => "Last Kingdom Standing";

    public MatchResult Evaluate()
    {
        var participants = AllParticipants();
        if (participants.Count < 2) return null;

        var alive = new List<PlayerInfo>();
        foreach (var p in participants)
        {
            if (IsKingdomAlive(p)) alive.Add(p);
        }

        if (alive.Count == 0)
            return new MatchResult { winnerId = -1, reason = "all kingdoms destroyed — draw" };
        if (alive.Count == 1)
            return new MatchResult { winnerId = alive[0].id, reason = "last kingdom standing" };
        return null;
    }

    private static List<PlayerInfo> AllParticipants()
    {
        var list = new List<PlayerInfo>();
        if (PlayerRegistry.Self.kingdomId >= 0) list.Add(PlayerRegistry.Self);
        foreach (var p in PlayerRegistry.Peers.Values)
            if (p.kingdomId >= 0) list.Add(p);
        return list;
    }

    private static bool IsKingdomAlive(PlayerInfo p)
    {
        var map = MapBox.instance;
        if (map == null || map.kingdoms == null) return false;
        var k = map.kingdoms.getCivOrWildViaID(p.kingdomId);
        if (k == null) return false;
        return k.cities != null && k.cities.Count > 0;
    }
}
