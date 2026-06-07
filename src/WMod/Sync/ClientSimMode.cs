using UnityEngine;

namespace WMod.Sync;

// Plan B-B Step 1: when this peer is a client (receiver), force Config.paused
// to true every frame so the local WorldBox simulation never advances. The
// host's state arrives via the existing snapshot/delta channel and is the
// single source of truth. Rendering and UI keep running normally.
internal static class ClientSimMode
{
    public static bool ForceClientPause;
    private static bool _wasPausedBefore;

    public static void Enable()
    {
        if (ForceClientPause) return;
        _wasPausedBefore = Config.paused;
        ForceClientPause = true;
        Config.paused = true;
        Debug.Log("[WMod] ClientSimMode ON: Config.paused forced true");
    }

    public static void Disable()
    {
        if (!ForceClientPause) return;
        ForceClientPause = false;
        // Restore prior state — don't leave the player paused after a disconnect.
        Config.paused = _wasPausedBefore;
        Debug.Log($"[WMod] ClientSimMode OFF: Config.paused restored to {_wasPausedBefore}");
    }

    public static void Tick()
    {
        // Re-assert each frame; some game systems try to flip paused back to false.
        if (ForceClientPause && !Config.paused) Config.paused = true;
    }
}
