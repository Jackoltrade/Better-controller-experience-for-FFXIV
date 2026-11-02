// SPDX-License-Identifier: GPL-3.0-only
// Hue conversion adapted from DS4Windows DS4LightBar.HuetoRGB. See THIRD-PARTY-NOTICES.txt.
// Original DS4Windows portions Copyright (C) 2023 Travis Nickles.
#nullable enable
using System;

namespace DualSenseLighting.Core;

public static class LightingEffects
{
    public static LightingFrame Render(LightingSettings settings, double seconds)
    {
        var s = settings.Normalize();
        var phase = double.IsFinite(seconds) ? Math.Max(0, seconds) % s.CycleSeconds / s.CycleSeconds : 0;
        var color = s.Effect == LightingEffect.Rainbow
            ? HueToRgb((float)(phase * 360)) : new LightingFrame(s.Red, s.Green, s.Blue, s.PlayerLights);
        var gain = s.Brightness;
        if (s.Effect == LightingEffect.Breathing)
            gain *= (float)((1 + Math.Cos(phase * Math.PI * 2)) / 2);
        return new(Scale(color.Red, gain), Scale(color.Green, gain), Scale(color.Blue, gain), s.PlayerLights);
    }

    private static byte Scale(byte value, float gain) => (byte)Math.Clamp((int)Math.Round(value * gain), 0, 255);

    private static LightingFrame HueToRgb(float hue)
    {
        const byte c = 255;
        var x = (byte)(c * (1 - Math.Abs(hue / 60 % 2 - 1)));
        return hue switch
        {
            < 60 => new(c, x, 0, 0),
            < 120 => new(x, c, 0, 0),
            < 180 => new(0, c, x, 0),
            < 240 => new(0, x, c, 0),
            < 300 => new(x, 0, c, 0),
            _ => new(c, 0, x, 0),
        };
    }
}
