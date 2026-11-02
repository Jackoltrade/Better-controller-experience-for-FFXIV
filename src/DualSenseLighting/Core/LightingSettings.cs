// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

// Append effects so saved Solid/Rainbow/Breathing selections retain their numeric values.
public enum LightingEffect { Solid, Rainbow, Breathing, ColorShift, Aurora, Fire, Heartbeat, Comet, RainbowPulse, SfxLoudness }

public sealed record LightingSettings(
    bool Enabled, LightingEffect Effect, byte Red, byte Green, byte Blue,
    float Brightness, float CycleSeconds, byte PlayerLights,
    PlayerIndicatorAnimation PlayerAnimation = PlayerIndicatorAnimation.Steady, float PlayerCycleSeconds = 2,
    byte SecondaryRed = 0, byte SecondaryGreen = 160, byte SecondaryBlue = 255,
    float SfxSensitivity = 8, float SfxNoiseGate = 0.002f, float SfxReleaseSeconds = 0.18f, bool SfxAutomaticGate = true)
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
        SfxSensitivity = float.IsFinite(SfxSensitivity) ? Math.Clamp(SfxSensitivity, 1, 32) : 8,
        SfxNoiseGate = float.IsFinite(SfxNoiseGate) ? Math.Clamp(SfxNoiseGate, 0, 0.05f) : 0.002f,
        SfxReleaseSeconds = float.IsFinite(SfxReleaseSeconds) ? Math.Clamp(SfxReleaseSeconds, 0.05f, 1) : 0.18f,
    };
}

public readonly record struct LightingFrame(byte Red, byte Green, byte Blue, byte PlayerLights, bool PlayerLightsImmediate = false);
