using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using WMod.Net;

namespace WMod.Sync;

internal class ClickPayload
{
    public string powerId { get; set; }
    public int x { get; set; }
    public int y { get; set; }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.clickedFinal))]
public static class ClickedFinalPatch
{
    [HarmonyPostfix]
    public static void Postfix(Vector2Int pPos, GodPower pPower, bool pTrack)
    {
        if (RemoteApplyGuard.IsApplyingRemote) return;
        if (pPower == null) return;
        if (NetworkManager.Role == NetRole.None) return;

        var payload = JsonConvert.SerializeObject(new ClickPayload
        {
            powerId = pPower.id,
            x = pPos.x,
            y = pPos.y,
        });
        NetworkManager.Send(NetMessage.Create("CLICK", payload));
        Debug.Log($"[WMod] click broadcast: {pPower.id} @ ({pPos.x},{pPos.y})");
    }
}

internal static class GodPowerSync
{
    public static void HandleRemoteClick(string payload)
    {
        try
        {
            var p = JsonConvert.DeserializeObject<ClickPayload>(payload);
            if (p == null || string.IsNullOrEmpty(p.powerId)) return;

            var power = AssetManager.powers.get(p.powerId);
            if (power == null) { Debug.Log($"[WMod] unknown power id: {p.powerId}"); return; }

            var map = MapBox.instance;
            if (map == null) { Debug.Log("[WMod] MapBox not ready"); return; }

            var tile = map.GetTile(p.x, p.y);
            if (tile == null) { Debug.Log($"[WMod] no tile at ({p.x},{p.y})"); return; }

            using (RemoteApplyGuard.Scope())
            {
                power.click_power_action?.Invoke(tile, power);
                power.click_action?.Invoke(tile, power.id);
            }
            Debug.Log($"[WMod] click applied remotely: {p.powerId} @ ({p.x},{p.y})");
        }
        catch (System.Exception ex)
        {
            Debug.Log($"[WMod] HandleRemoteClick error: {ex}");
        }
    }
}
