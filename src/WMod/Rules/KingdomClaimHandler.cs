using Newtonsoft.Json;
using WMod.Net;

namespace WMod.Rules;

internal class ClaimPayload
{
    public long kingdomId { get; set; }
    public string kingdomName { get; set; }
}

internal static class KingdomClaimHandler
{
    public static string TryClaimAtCursor()
    {
        if (PlayerRegistry.Self.id < 0)
            return "not connected (press Numpad 1 or 2 first)";

        var map = MapBox.instance;
        if (map == null) return "MapBox not ready";

        var tile = map.getMouseTilePos();
        if (tile == null) return "no tile under cursor";

        var kingdom = tile.zone?.city?.kingdom;
        if (kingdom == null) return "no kingdom on this tile (hover over a city)";

        PlayerRegistry.Self.kingdomId = kingdom.id;
        PlayerRegistry.Self.kingdomName = kingdom.name;

        var payload = JsonConvert.SerializeObject(new ClaimPayload
        {
            kingdomId = kingdom.id,
            kingdomName = kingdom.name,
        });
        NetworkManager.Send(NetMessage.Create("CLAIM", payload));
        return $"claimed {kingdom.name} (id={kingdom.id})";
    }

    public static void HandleRemoteClaim(int fromPlayerId, string payload)
    {
        if (fromPlayerId < 0) return;
        try
        {
            var p = JsonConvert.DeserializeObject<ClaimPayload>(payload);
            if (p == null) return;
            PlayerRegistry.UpdatePeer(fromPlayerId, p.kingdomId, p.kingdomName);
        }
        catch { }
    }
}
