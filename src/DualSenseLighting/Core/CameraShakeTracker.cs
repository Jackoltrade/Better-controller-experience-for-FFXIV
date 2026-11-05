// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;

namespace DualSenseLighting.Core;

/// <summary>Follows current authored shake offsets without a historical derivative delay.</summary>
internal sealed class CameraShakeTracker
{
    private const float NumericalTolerance = 0.000001f;
    private const float MinimumDrive = 0.16f;
    private Vector3 position, rotation;
    private long timestamp;
    private bool hasPosition;
    public long IntervalTicks { get; private set; }
    public float RawMagnitude { get; private set; }
    public int UnchangedFrames { get; private set; }
    public long LastChangeTimestamp { get; private set; }

    public float Observe(Vector3 currentPosition, Vector3 currentRotation, long now)
    {
        if (!Valid(currentPosition) || !Valid(currentRotation) || now < 0)
        { Reset(); return 0; }
        if (hasPosition && now <= timestamp) { Reset(); return 0; }
        var interval = hasPosition ? now - timestamp : 0;
        if (interval > ScreenEffectHaptics.FreshTicks) { Reset(); interval = 0; }
        RawMagnitude = Magnitude(currentPosition, currentRotation);
        var changed = !hasPosition || Magnitude(currentPosition - position, currentRotation - rotation) > NumericalTolerance;
        if (changed) { LastChangeTimestamp = now; UnchangedFrames = 0; }
        else if (UnchangedFrames < int.MaxValue) UnchangedFrames++;
        position = currentPosition; rotation = currentRotation;
        timestamp = now; IntervalTicks = interval; hasPosition = true;
        if (RawMagnitude <= NumericalTolerance) return 0;
        // Some effect tracks update less often than the camera or briefly hold an
        // extremum. Keep those frames, but let a genuinely static offset settle.
        var hold = Math.Clamp(Math.Max(interval * 2, Stopwatch.Frequency / 30), 1, ScreenEffectHaptics.FreshTicks);
        if (!changed && now - LastChangeTimestamp >= hold) return 0;
        // Lift real light shakes while preserving graded intensity and the
        // caller's strength cap. The minimum never applies to numerical silence.
        return MinimumDrive + (1 - MinimumDrive) * (RawMagnitude / (1 + RawMagnitude));
    }

    private static float Magnitude(Vector3 translation, Vector3 rotation) => translation.Length() * 4 + rotation.Length() * 2;

    private static bool Valid(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
        Math.Abs(value.X) <= 100 && Math.Abs(value.Y) <= 100 && Math.Abs(value.Z) <= 100;

    public void Reset()
    {
        position = rotation = Vector3.Zero;
        timestamp = IntervalTicks = LastChangeTimestamp = 0;
        RawMagnitude = 0; UnchangedFrames = 0;
        hasPosition = false;
    }
}
