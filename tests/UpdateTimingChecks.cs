// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using DualSenseLighting.Core;

internal static class UpdateTimingChecks
{
    private sealed class Clock
    {
        internal long Now, Step;
        internal int Reads;
        internal long Read() { Reads++; var value = Now; Now += Step; return value; }
    }

    internal static void Run(Action<bool, string> check)
    {
        CheckDisabled(check);
        CheckPhases(check);
        CheckEarlyExitAndFailure(check);
        CheckThresholds(check);
        CheckCap(check);
        CheckAllocations(check);
    }

    private static void CheckDisabled(Action<bool, string> check)
    {
        var clock = new Clock(); var timing = new UpdateTiming(clock.Read, 1_000_000);
        var frame = timing.Begin(false);
        frame.Next(UpdateTiming.Phase.Observers); frame.Next(UpdateTiming.Phase.SaveGuard);
        frame.Complete(); frame.Complete();
        check(clock.Reads == 0 && timing.ReadTotal() == default && timing.ReadPhase(UpdateTiming.Phase.Observers) == default,
            "Disabled update timing read a clock or recorded an interval");
    }

    private static void CheckPhases(Action<bool, string> check)
    {
        var clock = new Clock { Now = 100 }; var timing = new UpdateTiming(clock.Read, 1_000_000);
        var frame = timing.Begin(true);
        check(timing.ReadTotal().Count == 0, "An in-progress timing reservation was counted as a completed update");
        clock.Now = 110; frame.Next(UpdateTiming.Phase.Observers);
        clock.Now = 125; frame.Next(UpdateTiming.Phase.RosterArena);
        clock.Now = 145; frame.Complete();
        var total = timing.ReadTotal(); var phase = timing.ReadPhase(UpdateTiming.Phase.RecordingCoordination);
        check(total.Count == 1 && total.TotalTicks == 45 && total.MaxTicks == 45 && clock.Reads == 4,
            "Whole-update duration or timestamp boundaries were incorrect");
        check(phase == new UpdateTiming.PhaseTotals(1, 10, 10) &&
            timing.ReadPhase(UpdateTiming.Phase.Observers) == new UpdateTiming.PhaseTotals(1, 15, 15) &&
            timing.ReadPhase(UpdateTiming.Phase.RosterArena) == new UpdateTiming.PhaseTotals(1, 20, 20) &&
            timing.ReadPhase(UpdateTiming.Phase.PlayerPublication) == default,
            "Phase boundaries lost elapsed ticks or charged a skipped phase");
        var reads = clock.Reads;
        frame.Complete(); frame.Next(UpdateTiming.Phase.SaveGuard);
        _ = timing.ReadTotal(); _ = timing.ReadPhase(UpdateTiming.Phase.RosterArena);
        check(clock.Reads == reads && timing.ReadTotal() == total,
            "Completing the same timing variable twice or reading snapshots performed timing work");
        clock.Now = 200; var next = timing.Begin(true); clock.Now = 205; next.Complete();
        check(total.Count == 1 && total.TotalTicks == 45 && phase.TotalTicks == 10 && timing.ReadTotal().Count == 2,
            "Readonly timing snapshot values changed after a later update");
    }

    private static void CheckEarlyExitAndFailure(Action<bool, string> check)
    {
        var clock = new Clock(); var timing = new UpdateTiming(clock.Read, 1_000_000);
        EarlyReturn(timing, clock);
        check(timing.ReadTotal().TotalTicks == 7 && timing.ReadPhase(UpdateTiming.Phase.RecordingCoordination).TotalTicks == 7,
            "An early return failed to charge its active phase and whole update");
        var original = new InvalidOperationException("timing-body sentinel");
        Exception? observed = null;
        var frame = timing.Begin(true);
        try
        {
            clock.Now += 3; frame.Next(UpdateTiming.Phase.RecordingSample);
            clock.Now += 11; throw original;
        }
        catch (Exception error) { observed = error; }
        finally { frame.Complete(); }
        check(ReferenceEquals(original, observed) && timing.ReadTotal().Count == 2 && timing.ReadTotal().TotalTicks == 21 &&
            timing.ReadPhase(UpdateTiming.Phase.RecordingSample) == new UpdateTiming.PhaseTotals(1, 11, 11),
            "A throwing update lost its exception or finally failed to record the active phase");
    }

    private static void EarlyReturn(UpdateTiming timing, Clock clock)
    {
        var frame = timing.Begin(true);
        try { clock.Now += 7; return; }
        finally { frame.Complete(); }
    }

    private static void CheckThresholds(Action<bool, string> check)
    {
        var clock = new Clock(); var timing = new UpdateTiming(clock.Read, 1_000_000);
        foreach (var duration in new long[] { 999, 1000, 4999, 5000, 16699, 16700 })
        {
            var frame = timing.Begin(true); clock.Now += duration; frame.Complete();
        }
        var total = timing.ReadTotal();
        check(total == new UpdateTiming.Totals(6, 45397, 16700, 5, 3, 1),
            "Update tail thresholds lost equality/just-below boundaries at 1/5/16.7ms");
        var fractional = new UpdateTiming(clock.Read, 1001);
        foreach (var duration in new long[] { 1, 2, 5, 6, 16, 17 })
        {
            var frame = fractional.Begin(true); clock.Now += duration; frame.Complete();
        }
        var rounded = fractional.ReadTotal();
        check(rounded.AtLeast1Ms == 5 && rounded.AtLeast5Ms == 3 && rounded.AtLeast16_7Ms == 1,
            "Threshold tick conversion rounded down rather than using ceiling");
    }

    private static void CheckCap(Action<bool, string> check)
    {
        var clock = new Clock(); var timing = new UpdateTiming(clock.Read, 1_000_000, sampleLimit: 2);
        var first = timing.Begin(true); var second = timing.Begin(true);
        check(timing.ReadTotal().Count == 0 && clock.Reads == 2, "Reserved slots became completed frames");
        var capped = timing.Begin(true); capped.Next(UpdateTiming.Phase.Observers); capped.Complete();
        first.Complete();
        var reads = clock.Reads;
        var stillCapped = timing.Begin(true); stillCapped.Next(UpdateTiming.Phase.SaveGuard); stillCapped.Complete();
        check(clock.Reads == reads && timing.ReadTotal().Count == 1,
            "A cap reservation was reused or a capped frame read the clock");
        second.Complete();
        check(timing.ReadTotal().Count == 2, "Cap accounting dropped an already admitted frame");
    }

    private static void CheckAllocations(Action<bool, string> check)
    {
        var clock = new Clock { Step = 1 }; var timing = new UpdateTiming(clock.Read, 1_000_000);
        Action enabled = () => AllPhases(timing, true);
        Action disabled = () => AllPhases(timing, false);
        var enabledBytes = AllocationChecks.MeasureAllocations(enabled, 2000, 10000);
        check(enabledBytes == 0, $"Enabled phase timing allocated {enabledBytes} managed bytes");
        var reads = clock.Reads;
        var disabledBytes = AllocationChecks.MeasureAllocations(disabled, 2000, 10000);
        check(disabledBytes == 0 && clock.Reads == reads, $"Disabled phase timing allocated {disabledBytes} bytes or read the clock");
        var real = new UpdateTiming();
        Action realClock = () => AllPhases(real, true);
        var realBytes = AllocationChecks.MeasureAllocations(realClock, 2000, 10000);
        check(realBytes == 0, $"Real Stopwatch phase timing allocated {realBytes} managed bytes");
        var total = timing.ReadTotal();
        check(total.Count == 12000 && total.TotalTicks == 84000 && clock.Reads == 96000,
            "Full seven-phase timing did not preserve the exact clock/read/sample budget");
    }

    internal static void AllPhases(UpdateTiming timing, bool enabled)
    {
        var frame = timing.Begin(enabled);
        frame.Next(UpdateTiming.Phase.Observers);
        frame.Next(UpdateTiming.Phase.PlayerPublication);
        frame.Next(UpdateTiming.Phase.RosterArena);
        frame.Next(UpdateTiming.Phase.ScreenEffects);
        frame.Next(UpdateTiming.Phase.RecordingSample);
        frame.Next(UpdateTiming.Phase.SaveGuard);
        frame.Complete();
    }
}
