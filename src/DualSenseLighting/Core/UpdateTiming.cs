// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Threading;

namespace DualSenseLighting.Core;

/// <summary>Opt-in elapsed Update intervals. Numeric reads are individually atomic, not a coherent frame snapshot.</summary>
internal sealed class UpdateTiming
{
    internal enum Phase { RecordingCoordination, Observers, PlayerPublication, RosterArena, ScreenEffects, RecordingSample, SaveGuard }
    internal const int PhaseCount = 7;
    internal readonly record struct PhaseTotals(long Count, long TotalTicks, long MaxTicks);
    internal readonly record struct Totals(long Count, long TotalTicks, long MaxTicks,
        long AtLeast1Ms, long AtLeast5Ms, long AtLeast16_7Ms);

    private static readonly Func<long> RealClock = Stopwatch.GetTimestamp;
    private readonly Func<long> timestamp;
    private readonly long limit, oneMs, fiveMs, frameBudget;
    private readonly long[] phaseCounts = new long[PhaseCount], phaseTicks = new long[PhaseCount], phaseMax = new long[PhaseCount];
    private long admitted, count, ticks, maximum, atLeast1, atLeast5, atLeastFrame;

    internal UpdateTiming() : this(RealClock, Stopwatch.Frequency) { }

    // The injected clock is monotonic; only tests use a frequency other than Stopwatch.Frequency.
    internal UpdateTiming(Func<long> timestamp, long frequency, int sampleLimit = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(timestamp);
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        if (sampleLimit <= 0) throw new ArgumentOutOfRangeException(nameof(sampleLimit));
        this.timestamp = timestamp;
        limit = sampleLimit;
        oneMs = CeilingTicks(frequency, 1, 1000);
        fiveMs = CeilingTicks(frequency, 1, 200);
        frameBudget = CeilingTicks(frequency, 167, 10000);
    }

    internal Frame Begin(bool enabled)
    {
        if (!enabled) return default;
        var reserved = Interlocked.Read(ref admitted);
        while (reserved < limit)
        {
            var observed = Interlocked.CompareExchange(ref admitted, reserved + 1, reserved);
            if (observed == reserved) return new Frame(this, timestamp());
            reserved = observed;
        }
        return default;
    }

    internal Totals ReadTotal() => new(Interlocked.Read(ref count), Interlocked.Read(ref ticks), Interlocked.Read(ref maximum),
        Interlocked.Read(ref atLeast1), Interlocked.Read(ref atLeast5), Interlocked.Read(ref atLeastFrame));

    internal PhaseTotals ReadPhase(Phase phase)
    {
        var index = (int)phase;
        return new(Interlocked.Read(ref phaseCounts[index]), Interlocked.Read(ref phaseTicks[index]), Interlocked.Read(ref phaseMax[index]));
    }

    private static long CeilingTicks(long frequency, long numerator, long denominator) =>
        frequency / denominator * numerator + (frequency % denominator * numerator + denominator - 1) / denominator;

    private void RecordPhase(Phase phase, long elapsed)
    {
        var index = (int)phase;
        Interlocked.Add(ref phaseTicks[index], elapsed);
        RaiseMax(ref phaseMax[index], elapsed);
        Interlocked.Increment(ref phaseCounts[index]);
    }

    private void RecordTotal(long elapsed)
    {
        Interlocked.Add(ref ticks, elapsed);
        RaiseMax(ref maximum, elapsed);
        if (elapsed >= oneMs) Interlocked.Increment(ref atLeast1);
        if (elapsed >= fiveMs) Interlocked.Increment(ref atLeast5);
        if (elapsed >= frameBudget) Interlocked.Increment(ref atLeastFrame);
        Interlocked.Increment(ref count); // Count completed intervals, never reserved/in-progress frames.
    }

    private static void RaiseMax(ref long destination, long value)
    {
        var current = Interlocked.Read(ref destination);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref destination, value, current);
            if (observed == current) return;
            current = observed;
        }
    }

    // Mutate and complete the SAME variable; pass it by ref across UpdateCore.
    // Copying a value copies its ownership and is not a second independent completion token.
    internal struct Frame
    {
        private UpdateTiming? owner;
        private readonly long started;
        private long phaseStarted;
        private Phase phase;

        internal Frame(UpdateTiming owner, long started)
        {
            this.owner = owner;
            this.started = phaseStarted = started;
            phase = Phase.RecordingCoordination;
        }

        internal void Next(Phase next)
        {
            var current = owner;
            if (current == null) return;
            if ((uint)next >= PhaseCount) throw new ArgumentOutOfRangeException(nameof(next));
            var now = current.timestamp();
            current.RecordPhase(phase, now - phaseStarted);
            phase = next;
            phaseStarted = now;
        }

        internal void Complete()
        {
            var current = owner;
            if (current == null) return;
            owner = null;
            var now = current.timestamp();
            current.RecordPhase(phase, now - phaseStarted);
            current.RecordTotal(now - started);
        }
    }
}
