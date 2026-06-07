using HarmonyLib;
using UnityEngine;

namespace WMod.Sync;

// Phase D Step 1: pin every simulation tick to the same length on both peers.
// We patch Time.deltaTime to always return 1/TargetHz and cap rendering to
// TargetHz via Application.targetFrameRate. With Phase 2's sequential
// execution and a shared Randy seed, both peers should run identical
// simulations as long as both can hit TargetHz.
internal static class FixedTick
{
    public static int TargetHz = 30;
    public static bool Enabled;

    public static void Toggle()
    {
        Enabled = !Enabled;
        if (Enabled)
        {
            Application.targetFrameRate = TargetHz;
            QualitySettings.vSyncCount = 0;
        }
        else
        {
            Application.targetFrameRate = -1;
        }
        WModBridge.Toast($"[Numpad /] FixedTick: {(Enabled ? $"ON @ {TargetHz}Hz (deltaTime = {1f / TargetHz:F4})" : "OFF")}");
        Debug.Log($"[WMod] FixedTick.Enabled={Enabled} Hz={TargetHz}");
    }
}

[HarmonyPatch(typeof(Time), "get_deltaTime")]
public static class TimeDeltaTimePatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref float __result)
    {
        if (!FixedTick.Enabled) return true;
        __result = 1f / FixedTick.TargetHz;
        return false;
    }
}

// smoothDeltaTime is sometimes used by simulation code for jitter-smoothed dt.
[HarmonyPatch(typeof(Time), "get_smoothDeltaTime")]
public static class TimeSmoothDeltaTimePatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref float __result)
    {
        if (!FixedTick.Enabled) return true;
        __result = 1f / FixedTick.TargetHz;
        return false;
    }
}
