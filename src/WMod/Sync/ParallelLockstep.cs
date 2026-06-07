using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace WMod.Sync;

// Phase 2: force Parallel.For/ForEach to run sequentially so any Randy state
// the worker delegates consume is consumed in deterministic order across peers.
internal static class ParallelLockstep
{
    public static bool ForceSequential;

    public static void Toggle()
    {
        ForceSequential = !ForceSequential;
        WModBridge.Toast($"[Numpad *] Parallel: {(ForceSequential ? "SEQUENTIAL" : "PARALLEL")}");
        Debug.Log($"[WMod] ParallelLockstep.ForceSequential={ForceSequential}");
    }
}

public static class WModSeqHelper
{
    public static ParallelLoopResult SeqFor(int fromInclusive, int toExclusive, Action<int> body)
    {
        if (!ParallelLockstep.ForceSequential)
            return Parallel.For(fromInclusive, toExclusive, body);
        for (int i = fromInclusive; i < toExclusive; i++) body(i);
        return default;
    }

    public static ParallelLoopResult SeqForEach<T>(IEnumerable<T> source, Action<T> body)
    {
        if (!ParallelLockstep.ForceSequential)
            return Parallel.ForEach(source, body);
        foreach (var item in source) body(item);
        return default;
    }
}

// Transpiler that rewrites Parallel.For / Parallel.ForEach<T> call sites inside
// targeted methods to go through WModSeqHelper.SeqFor / SeqForEach instead.
[HarmonyPatch]
public static class ParallelCallSiteRewrite
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        var results = new List<MethodBase>();
        // Per-frame sim hotspots (most important)
        TryAdd(results, "MapBox", "checkDirtyUnits");
        // Chunk topology
        TryAdd(results, "MapChunkManager", "calc_tileEdges");
        TryAdd(results, "MapChunkManager", "calc_regions");
        TryAdd(results, "MapChunkManager", "calc_linkedRegions");
        TryAdd(results, "MapChunkManager", "calc_links");
        TryAdd(results, "MapChunkManager", "calc_neighbourIslands");
        // Rendering precalc (still ordering-sensitive if it reads simulation state)
        TryAdd(results, "ActorManager", "precalculateRenderDataParallel");
        TryAdd(results, "BuildingManager", "precalculateRenderDataParallel");
        // AI job processing — patch via concrete subclasses to avoid the open-
        // generic Transpiler crash we hit earlier.
        TryAddInherited(results, "JobManagerActors", "updateBaseJobsParallel");
        TryAddInherited(results, "JobManagerBuildings", "updateBaseJobsParallel");
        return results;
    }

    private static void TryAdd(List<MethodBase> list, string typeName, string methodName)
    {
        var t = Type.GetType(typeName + ", Assembly-CSharp")
                ?? AccessTools.TypeByName(typeName);
        if (t == null) { Debug.Log($"[WMod] ParallelCallSiteRewrite: type {typeName} not found"); return; }
        var m = AccessTools.Method(t, methodName);
        if (m == null) { Debug.Log($"[WMod] ParallelCallSiteRewrite: {typeName}.{methodName} not found"); return; }
        list.Add(m);
    }

    private static void TryAddInherited(List<MethodBase> list, string typeName, string methodName)
    {
        // For methods inherited from a generic base, resolve the closed-generic
        // method via the concrete subclass — avoids the open-generic Transpiler
        // crash we hit when targeting JobManagerBase`2 directly.
        var t = AccessTools.TypeByName(typeName);
        if (t == null) { Debug.Log($"[WMod] ParallelCallSiteRewrite: type {typeName} not found"); return; }
        var m = t.GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
        if (m == null) { Debug.Log($"[WMod] ParallelCallSiteRewrite: {typeName}.{methodName} not found"); return; }
        list.Add(m);
        Debug.Log($"[WMod] ParallelCallSiteRewrite: targeting {typeName}.{methodName} (declared on {m.DeclaringType.Name})");
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var seqFor = AccessTools.Method(typeof(WModSeqHelper), nameof(WModSeqHelper.SeqFor));
        var seqForEachOpen = AccessTools.Method(typeof(WModSeqHelper), nameof(WModSeqHelper.SeqForEach));

        foreach (var inst in instructions)
        {
            if (inst.opcode == OpCodes.Call && inst.operand is MethodInfo mi
                && mi.DeclaringType == typeof(Parallel))
            {
                if (mi.Name == "For" && !mi.IsGenericMethod
                    && mi.GetParameters().Length == 3
                    && mi.GetParameters()[0].ParameterType == typeof(int)
                    && mi.GetParameters()[1].ParameterType == typeof(int))
                {
                    yield return new CodeInstruction(OpCodes.Call, seqFor);
                    continue;
                }
                if (mi.Name == "ForEach" && mi.IsGenericMethod
                    && mi.GetParameters().Length == 2)
                {
                    var tArg = mi.GetGenericArguments()[0];
                    yield return new CodeInstruction(OpCodes.Call, seqForEachOpen.MakeGenericMethod(tArg));
                    continue;
                }
            }
            yield return inst;
        }
    }
}

// Keep the global Parallel.For prefix as a safety net for any uninstrumented site.
[HarmonyPatch(typeof(Parallel), nameof(Parallel.For), new Type[] { typeof(int), typeof(int), typeof(Action<int>) })]
public static class ParallelForGlobalPatch
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
