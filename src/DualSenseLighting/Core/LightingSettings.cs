// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

// Append effects so saved Solid/Rainbow/Breathing selections retain their numeric values.
public enum LightingEffect { Solid, Rainbow, Breathing, ColorShift, Aurora, Fire, Heartbeat, Comet, RainbowPulse }

public sealed record LightingSettings(
    bool Enabled, LightingEffect Effect, byte Red, byte Green, byte Blue,
    float Brightness, float CycleSeconds, byte PlayerLights,
    PlayerIndicatorAnimation PlayerAnimation = PlayerIndicatorAnimation.Steady, float PlayerCycleSeconds = 2,
    byte SecondaryRed = 0, byte SecondaryGreen = 160, byte SecondaryBlue = 255)
{
    public static LightingSettings Default { get; } = new(true, LightingEffect.Solid, 119, 255, 0, 1, 8, 0);

    public LightingSettings Normalize() => this with
    {
        Effect = Enum.IsDefined(Effect) ? Effect : LightingEffect.Solid,
        Brightness = float.IsFinite(Brightness) ? Math.Clamp(Brightness, 0, 1) : 1,
        CycleSeconds = float.IsFinite(CycleSeconds) ? Math.Clamp(CycleSeconds, 1, 60) : 8,
        PlayerLights = (byte)(PlayerLights & 0x1f),
        PlayerAnimation = Enum.IsDefined(PlayerAnimation) ? PlayerAnimation : PlayerIndicatorAnimation.Steady,
        PlayerCycleSeconds = float.IsFinite(PlayerCycleSeconds) ? Math.Clamp(PlayerCycleSeconds, 1, 12) : 2,
    };
}

public readonly record struct LightingFrame(byte Red, byte Green, byte Blue, byte PlayerLights, bool PlayerLightsImmediate = false);
