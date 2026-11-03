// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

/// <summary>One renderer for preview, native-report guard and controller worker.</summary>
public static class LimitBreakIndicators
{
    public static LightingFrame Render(LimitBreakState? state, double seconds, float cycleSeconds)
    {
        if (state is null || !state.IsFresh(seconds)) return new(0, 0, 0, 0, true);
        if (state.IsFlashing(seconds))
        {
            // Four flashes/second, with a fresh on-phase when the cast/use begins.
            var on = (long)Math.Floor(Math.Max(0, seconds - state.FlashStartedAt) * 8 + 1e-8) % 2 == 0;
            return new(0, 0, 0, on ? state.IsPvP ? (byte)27 : PveMask(state.BarCount) : (byte)0, true);
        }

        if (!state.IsPvP) return Blink(PveMask(state.ReadyBars), seconds, cycleSeconds);
        var count = state.ReadyQuarters;
        if (count == 4) return Blink(27, seconds, cycleSeconds);
        if (count == 0) return new(0, 0, 0, 0, true);
        if (count == 1) return Blink(1, seconds, cycleSeconds);

        var cycle = float.IsFinite(cycleSeconds) ? Math.Clamp(cycleSeconds, 1, 12) : 2;
        var phase = Math.Round(Math.Max(0, seconds) % cycle / cycle, 9);
        if (phase >= 1) phase = 0;
        // One gap moves directly between earned positions: restore one LED and clear the next in the same report.
        // Immediate updates avoid the native bank-wide fade that disturbed otherwise steady LEDs.
        var loading = phase * count;
        var index = Math.Min((int)(loading + 1e-8), count - 1);
        var bit = index < 2 ? index : index + 1; // Four-position layout leaves the center unused.
        var pool = count == 3 ? 11 : 3;
        return new(0, 0, 0, (byte)(pool & ~(1 << bit)), true);
    }

    private static byte PveMask(int count) => count switch { >= 3 => 21, 2 => 5, 1 => 1, _ => 0 };

    private static LightingFrame Blink(byte mask, double seconds, float cycleSeconds) =>
        new(0, 0, 0, PlayerIndicatorEffects.Render(mask, PlayerIndicatorAnimation.Blink, cycleSeconds, seconds), true);
}
