using System;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace WMod.Sync;

// Phase 2 prototype: force Parallel.For to run sequentially. Any RNG (Randy.rnd)
// the worker delegates consume is then consumed in deterministic order across peers.
// Parallel.ForEach is the harder generic case and is left for follow-up.
internal static class ParallelLockstep
{
    public static bool ForceSequential;

    public static void Toggle()
    {
        ForceSequential = !ForceSequential;
        WModBridge.Toast($"[Numpad *] Parallel.For: {(ForceSequential ? "SEQUENTIAL" : "PARALLEL")}");
        Debug.Log($"[WMod] ParallelLockstep.ForceSequential={ForceSequential}");
    }
}

[HarmonyPatch(typeof(Parallel), nameof(Parallel.For), new Type[] { typeof(int), typeof(int), typeof(Action<int>) })]
public static class ParallelForPatch
{
    [HarmonyPrefix]
    public static bool Prefix(int fromInclusive, int toExclusive, Action<int> body, ref ParallelLoopResult __result)
    {
        if (!ParallelLockstep.ForceSequential) return true;
        for (int i = fromInclusive; i < toExclusive; i++) body(i);
        __result = default;
        return false;
    }
}
