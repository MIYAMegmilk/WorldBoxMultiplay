using HarmonyLib;
using UnityEngine;

namespace WMod.Sync;

// Phase D Step 1: pin sim time to a single source of truth (TickCounter) so
// every Time.* read derives from the same number on a given peer. If both
// peers can sustain TargetHz frame rate, they advance in lockstep.
// True cross-peer lockstep still requires Step 3 (barrier sync).
internal static class FixedTick
{
    public static int TargetHz = 30;
    public static bool Enabled;
    public static long TickCounter;
    public static float TickDelta => 1f / TargetHz;

    public static void Toggle()
    {
        Enabled = !Enabled;
        if (Enabled)
        {
            Application.targetFrameRate = TargetHz;
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true; // don't throttle when the other window has focus
        }
        else
        {
            Application.targetFrameRate = -1;
        }
        WModBridge.Toast($"[Numpad /] FixedTick: {(Enabled ? $"ON @ {TargetHz}Hz" : "OFF")}");
        Debug.Log($"[WMod] FixedTick.Enabled={Enabled} Hz={TargetHz}");
    }

    public static void TickIfEnabled()
    {
        if (Enabled) TickCounter++;
    }
}

[HarmonyPatch(typeof(Time), "get_deltaTime")]
public static class TimeDeltaTimePatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref float __result)
    {
        if (!FixedTick.Enabled) return true;
        __result = FixedTick.TickDelta;
        return false;
    }
}

[HarmonyPatch(typeof(Time), "get_smoothDeltaTime")]
public static class TimeSmoothDeltaTimePatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref float __result)
    {
        if (!FixedTick.Enabled) return true;
        __result = FixedTick.TickDelta;
        return false;
    }
}

[HarmonyPatch(typeof(Time), "get_fixedDeltaTime")]
public static class TimeFixedDeltaTimePatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref float __result)
    {
        if (!FixedTick.Enabled) return true;
        __result = FixedTick.TickDelta;
        return false;
    }
}

[HarmonyPatch(typeof(Time), "get_time")]
public static class TimeTimePatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref float __result)
    {
        if (!FixedTick.Enabled) return true;
        __result = (float)(FixedTick.TickCounter * FixedTick.TickDelta);
        return false;
    }
}

[HarmonyPatch(typeof(Time), "get_realtimeSinceStartup")]
public static class TimeRealtimeSinceStartupPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref float __result)
    {
        if (!FixedTick.Enabled) return true;
        __result = (float)(FixedTick.TickCounter * FixedTick.TickDelta);
        return false;
    }
}

[HarmonyPatch(typeof(Time), "get_realtimeSinceStartupAsDouble")]
public static class TimeRealtimeSinceStartupAsDoublePatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref double __result)
    {
        if (!FixedTick.Enabled) return true;
        __result = FixedTick.TickCounter * (double)FixedTick.TickDelta;
        return false;
    }
}

[HarmonyPatch(typeof(Time), "get_frameCount")]
public static class TimeFrameCountPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ref int __result)
    {
        if (!FixedTick.Enabled) return true;
        __result = (int)FixedTick.TickCounter;
        return false;
    }
}
