// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Numerics;

namespace DualSenseLighting.Core;

/// <summary>Preserves full screen feedback whenever an independent effect is present.</summary>
internal sealed class ScreenEffectFilterGuard
{
    private long protectedUntil;

    internal float Apply(float candidate, Vector3 primaryPosition, Vector3 primaryRotation,
        float scriptedLevel, ScreenEffectKind effects, long now)
    {
        if (primaryPosition != Vector3.Zero || primaryRotation != Vector3.Zero || scriptedLevel > 0 ||
            (effects & ~ScreenEffectKind.Shake) != 0)
            protectedUntil = now + ScreenEffectHaptics.FreshTicks;
        if (now < protectedUntil || !float.IsFinite(candidate)) return 1;
        return Math.Clamp(candidate, 0, 1);
    }
}
