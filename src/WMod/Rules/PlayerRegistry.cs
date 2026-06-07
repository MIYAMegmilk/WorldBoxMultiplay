using System.Collections.Generic;
using System.Text;

namespace WMod.Rules;

internal static class PlayerRegistry
{
    public static readonly PlayerInfo Self = new PlayerInfo { id = -1, name = "Unknown" };
    public static readonly Dictionary<int, PlayerInfo> Peers = new Dictionary<int, PlayerInfo>();

    public static void SetSelf(int id, string name)
    {
        Self.id = id;
        if (!string.IsNullOrEmpty(name)) Self.name = name;
    }

    public static void UpdatePeer(int id, long kingdomId, string kingdomName)
    {
        if (id < 0 || id == Self.id) return;
        if (!Peers.TryGetValue(id, out var p))
        {
            p = new PlayerInfo { id = id, name = $"Peer{id}" };
            Peers[id] = p;
        }
        p.kingdomId = kingdomId;
        p.kingdomName = kingdomName;
    }

    public static void Reset()
    {
        Self.id = -1;
        Self.name = "Unknown";
        Self.kingdomId = -1;
        Self.kingdomName = null;
        Peers.Clear();
    }

    public static string RosterText()
    {
        var sb = new StringBuilder();
        sb.Append("You[");
        AppendOne(sb, Self);
        sb.Append("]");
        foreach (var p in Peers.Values)
        {
            sb.Append(" / [");
            AppendOne(sb, p);
            sb.Append("]");
        }
        return sb.ToString();
    }

    private static void AppendOne(StringBuilder sb, PlayerInfo p)
    {
        sb.Append("id=").Append(p.id).Append(' ').Append(p.name);
        sb.Append(" kd=");
        if (p.kingdomId < 0) sb.Append("-");
        else sb.Append(string.IsNullOrEmpty(p.kingdomName) ? p.kingdomId.ToString() : p.kingdomName);
    }
}
