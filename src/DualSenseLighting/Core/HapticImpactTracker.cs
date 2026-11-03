// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System.Diagnostics;
using System.Numerics;
using System.Threading;

namespace DualSenseLighting.Core;

public readonly record struct HapticImpactCue(int SoundId, Vector3 Position, long Timestamp);

/// <summary>One strongest impact per worker interval; audio callers never wait.</summary>
public sealed class HapticImpactTracker
{
    private int gate;
    private object? generation;
    private HapticImpactCue candidate;
    private float energy;

    public static bool Fresh(long timestamp, long now) => timestamp > 0 && now >= timestamp &&
        now - timestamp <= Stopwatch.Frequency / 10;

    public void Observe(object epoch, int soundId, Vector3 position, float rms, long now)
    {
        if (soundId <= 0 || !HapticSpatializer.ValidPosition(position) || !float.IsFinite(rms) || rms <= 0.002f ||
            Interlocked.CompareExchange(ref gate, 1, 0) != 0) return;
        try
        {
            if (!ReferenceEquals(generation, epoch) || !Fresh(candidate.Timestamp, now) || rms >= energy)
            { generation = epoch; candidate = new(soundId, position, now); energy = rms; }
        }
        finally { Volatile.Write(ref gate, 0); }
    }

    public bool TryTake(object epoch, long now, out HapticImpactCue cue)
    {
        cue = default;
        if (Interlocked.CompareExchange(ref gate, 1, 0) != 0) return false;
        try
        {
            var valid = ReferenceEquals(generation, epoch) && energy > 0 && Fresh(candidate.Timestamp, now);
            if (valid) cue = candidate;
            energy = 0;
            return valid;
        }
        finally { Volatile.Write(ref gate, 0); }
    }
}
