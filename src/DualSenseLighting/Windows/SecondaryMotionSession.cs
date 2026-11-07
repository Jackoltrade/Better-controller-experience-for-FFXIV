// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System.Threading;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>One foreground observation epoch sharing a fixed-pattern filter with the recorder.</summary>
internal sealed class SecondaryMotionSession
{
    internal readonly ScreenEffectSourceRecording? Recording;
    internal readonly KnownMotionFilter? Filter;
    private int retired;
    internal bool Active => Volatile.Read(ref retired) == 0;

    internal SecondaryMotionSession(ScreenEffectSourceRecording? recording, KnownMotionFilter? filter)
    { Recording = recording; Filter = filter; }

    internal void Observe(SecondaryVectorSample value, bool position, long clock)
    {
        if (!Active) return;
        if (position) Recording?.OnPosition(value, clock);
        else Recording?.OnRotation(value, clock);
        Filter?.Observe(value, position, clock);
    }

    internal void Reset(long source, long context, long config, uint cursor, uint state, long clock)
    {
        if (!Active) return;
        Recording?.OnReset(source, context, config, cursor, state, clock);
        Filter?.Realign(source, context, config, clock);
    }

    internal void Gap(long clock)
    {
        if (!Active) return;
        Recording?.OnGap(clock);
        Filter?.Invalidate();
    }

    internal void Retire(long clock)
    {
        if (Interlocked.Exchange(ref retired, 1) != 0) return;
        Recording?.OnGap(clock);
    }
}
