// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Runtime.CompilerServices;
using DualSenseLighting.Core;

internal static class AllocationChecks
{
    private static object? allocationSink;

    // Keep harness loop profiling/OSR outside the callback allocation contract.
    // NoInlining also prevents the caller from specializing the Action boundary.
    // The callback and production methods retain ordinary runtime tiering.
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    internal static long MeasureAllocations(Action step, int warmup, int iterations)
    {
        for (var i = 0; i < warmup; i++) step();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++) step();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    internal static void Run(Action<bool, string> check)
    {
        const int warmup = 2000, iterations = 10000;
        Action noop = static () => { };
        Action allocate = static () => allocationSink = new byte[37]; // Escapes to prevent stack allocation/elision.
        var cache = new EnemyEffectOwnership();
        var caster = new GameAudioLayout.PlayerIdentity(200, 2000, 20000);
        var timeline = new EnemyActionTag(EnemyActionKind.Skill, TimelineId: 3216, Phase: EnemyActionPhase.Execution);
        nint instance = 1000;
        Action changeAssociation = () => cache.Observe(100, ++instance, caster, timeline);

        var emptyBytes = MeasureAllocations(noop, warmup, iterations);
        check(emptyBytes == 0, $"No-op measurement callback allocated {emptyBytes} bytes");
        var positiveBytes = MeasureAllocations(allocate, warmup, iterations);
        check(positiveBytes == 64L * iterations,
            $"Escaping byte[37] control allocated {positiveBytes} bytes; expected {64L * iterations} on x64");
        GC.KeepAlive(allocationSink);
        var changedBytes = MeasureAllocations(changeAssociation, warmup, iterations);
        check(changedBytes > 0 && cache.Resolve(100, instance, caster).IsReleasedEffect,
            $"Real changing cache associations were lost or their allocations hidden ({changedBytes} bytes)");
    }
}
