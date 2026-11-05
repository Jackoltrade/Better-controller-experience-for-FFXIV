// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Threading;

namespace DualSenseLighting.Core;

/// <summary>One atomic stereo peak interval. Reset retires the entire accumulator.</summary>
public sealed class StereoRumbleSignal
{
    public sealed class Epoch
    {
        internal long Peaks;
        internal readonly bool Active;
        internal Epoch(bool active) => Active = active;
    }

    private Epoch epoch = new(false);
    public Epoch CurrentEpoch => Volatile.Read(ref epoch);
    public bool Active => CurrentEpoch.Active;

    public void SetActive(bool active)
    {
        while (true)
        {
            var before = CurrentEpoch;
            if (before.Active == active) return;
            if (ReferenceEquals(Interlocked.CompareExchange(ref epoch, new Epoch(active), before), before)) return;
        }
    }

    public void Observe(float left, float right, Epoch captured)
    {
        if (!captured.Active || !ReferenceEquals(captured, CurrentEpoch) ||
            !float.IsFinite(left) || !float.IsFinite(right)) return;
        var leftBits = BitConverter.SingleToUInt32Bits(left <= 0 ? 0 : Math.Min(left, 1));
        var rightBits = BitConverter.SingleToUInt32Bits(right <= 0 ? 0 : Math.Min(right, 1));
        var before = Volatile.Read(ref captured.Peaks);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var after = (long)((ulong)Math.Max((uint)before, leftBits) |
                ((ulong)Math.Max((uint)((ulong)before >> 32), rightBits) << 32));
            if (after == before) return;
            var current = Interlocked.CompareExchange(ref captured.Peaks, after, before);
            if (current == before) return;
            before = current;
        }
    }

    public (float Left, float Right) TakePeak()
    {
        var captured = CurrentEpoch;
        var peaks = Interlocked.Exchange(ref captured.Peaks, 0);
        // Fault/reset may originate on the audio thread while the worker consumes.
        if (!captured.Active || !ReferenceEquals(captured, CurrentEpoch)) return default;
        return (BitConverter.UInt32BitsToSingle((uint)peaks),
            BitConverter.UInt32BitsToSingle((uint)((ulong)peaks >> 32)));
    }
}
