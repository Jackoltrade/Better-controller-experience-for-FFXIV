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
        // These serial tests reserve a process-wide no-GC interval to isolate
        // callback allocations from context retirement. Setup stays outside the
        // counters; reservation failure or budget exhaustion must fail the check.
        if (!GC.TryStartNoGCRegion(16 * 1024 * 1024, disallowFullBlockingGC: true))
            throw new InvalidOperationException("Cannot reserve the allocation measurement's no-GC region.");
        Exception? failure = null;
        try
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; i++) step();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            try { GC.EndNoGCRegion(); }
            catch (Exception cleanup) when (failure != null)
            { throw new AggregateException("Allocation measurement and region cleanup failed.", failure, cleanup); }
        }
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

        var calls = 0;
        Action firstExecution = () =>
        {
            if (calls++ == 0) allocationSink = new byte[37];
        };
        var firstBytes = MeasureAllocations(firstExecution, warmup: 0, iterations: 1);
        check(firstBytes == 64 && calls == 1,
            $"Allocation setup executed the callback or hid its first allocation ({firstBytes} bytes, {calls} calls)");
        GC.KeepAlive(allocationSink);

        var propagated = false;
        try { MeasureAllocations(static () => throw new InvalidOperationException("allocation callback sentinel"), 0, 1); }
        catch (InvalidOperationException error) when (error.Message == "allocation callback sentinel") { propagated = true; }
        check(propagated && MeasureAllocations(noop, 0, 1) == 0,
            "Allocation measurement swallowed callback failure or retained its no-GC region");

        var retainedBoth = false;
        try
        {
            MeasureAllocations(static () =>
            {
                GC.EndNoGCRegion(); // Deliberately invalidate the owned region.
                throw new InvalidOperationException("callback and cleanup sentinel");
            }, 0, 1);
        }
        catch (AggregateException errors)
        {
            retainedBoth = errors.InnerExceptions.Count == 2 &&
                errors.InnerExceptions[0].Message == "callback and cleanup sentinel";
        }
        check(retainedBoth && MeasureAllocations(noop, 0, 1) == 0,
            "Region cleanup hid the callback failure or left later measurements unusable");
    }
}
