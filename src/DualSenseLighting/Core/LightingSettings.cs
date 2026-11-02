// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

public enum LightingEffect { Solid, Rainbow, Breathing }

public sealed record LightingSettings(
    bool Enabled, LightingEffect Effect, byte Red, byte Green, byte Blue,
    float Brightness, float CycleSeconds, byte PlayerLights)
{
    public static LightingSettings Default { get; } = new(true, LightingEffect.Solid, 119, 255, 0, 1, 8, 0);

    public LightingSettings Normalize() => this with
    {
        Effect = Enum.IsDefined(Effect) ? Effect : LightingEffect.Solid,
        Brightness = float.IsFinite(Brightness) ? Math.Clamp(Brightness, 0, 1) : 1,
        CycleSeconds = float.IsFinite(CycleSeconds) ? Math.Clamp(CycleSeconds, 1, 60) : 8,
        PlayerLights = (byte)(PlayerLights & 0x1f),
    };
}

public readonly record struct LightingFrame(byte Red, byte Green, byte Blue, byte PlayerLights);
