// SPDX-License-Identifier: GPL-3.0-only
// Hue conversion adapted from DS4Windows DS4LightBar.HuetoRGB. See THIRD-PARTY-NOTICES.txt.
// Original DS4Windows portions Copyright (C) 2023 Travis Nickles.
#nullable enable
using System;

namespace DualSenseLighting.Core;

public static class LightingEffects
{
    private static readonly LightingFrame[] AuroraColors =
        [new(0, 255, 160, 0), new(0, 180, 255, 0), new(105, 70, 255, 0), new(215, 40, 255, 0)];

    public static bool UsesPrimaryColor(LightingEffect effect) => effect is LightingEffect.Solid or
        LightingEffect.Breathing or LightingEffect.ColorShift or LightingEffect.Heartbeat or LightingEffect.Comet or LightingEffect.SfxLoudness;

    public static LightingFrame Render(LightingSettings settings, double seconds, float sfxLevel = 0)
    {
        var s = settings.Normalize();
        var phase = double.IsFinite(seconds) ? Math.Max(0, seconds) % s.CycleSeconds / s.CycleSeconds : 0;
        var primary = new LightingFrame(s.Red, s.Green, s.Blue, 0);
        var color = s.Effect switch
        {
            LightingEffect.Rainbow => HueToRgb((float)(phase * 360)),
            LightingEffect.Breathing => Dim(primary, Wave(phase)),
            LightingEffect.ColorShift => Mix(primary, new(s.SecondaryRed, s.SecondaryGreen, s.SecondaryBlue, 0), 1 - Wave(phase)),
            LightingEffect.Aurora => Dim(Palette(AuroraColors, phase), 0.65 + 0.35 * Wave(phase * 2)),
            LightingEffect.Fire => Fire(phase),
            LightingEffect.Heartbeat => Dim(primary, Math.Max(Pulse(phase, 0.04, 0.16), 0.7 * Pulse(phase, 0.28, 0.22))),
            LightingEffect.Comet => Dim(primary, phase < 0.06 ? Smooth(phase / 0.06) : Math.Pow((1 - phase) / 0.94, 3)),
            LightingEffect.RainbowPulse => Dim(HueToRgb((float)(phase * 360)), 0.15 + 0.85 * Wave(phase * 3)),
            LightingEffect.SfxLoudness => Dim(primary, float.IsFinite(sfxLevel) ? Math.Clamp(sfxLevel, 0, 1) : 0),
            _ => primary,
        };
        var indicators = PlayerIndicatorEffects.Render(s.PlayerLights, s.PlayerAnimation, s.PlayerCycleSeconds, seconds);
        return new(Scale(color.Red, s.Brightness), Scale(color.Green, s.Brightness), Scale(color.Blue, s.Brightness), indicators,
            s.PlayerAnimation != PlayerIndicatorAnimation.Steady);
    }

    private static byte Scale(byte value, double gain) => (byte)Math.Clamp((int)Math.Round(value * gain), 0, 255);

    private static LightingFrame Dim(LightingFrame color, double gain) =>
        new(Scale(color.Red, gain), Scale(color.Green, gain), Scale(color.Blue, gain), 0);

    private static LightingFrame Mix(LightingFrame from, LightingFrame to, double amount)
    {
        byte Channel(byte a, byte b) => (byte)Math.Clamp((int)Math.Round(a + (b - a) * amount), 0, 255);
        return new(Channel(from.Red, to.Red), Channel(from.Green, to.Green), Channel(from.Blue, to.Blue), 0);
    }

    private static double Wave(double phase) => (1 + Math.Cos(phase * Math.PI * 2)) / 2;
    private static double Smooth(double amount) => amount * amount * (3 - 2 * amount);

    private static double Pulse(double phase, double start, double width)
    {
        if (phase <= start || phase >= start + width) return 0;
        return (1 - Math.Cos((phase - start) / width * Math.PI * 2)) / 2;
    }

    private static LightingFrame Palette(LightingFrame[] colors, double phase)
    {
        var position = phase * colors.Length;
        var index = (int)position;
        return Mix(colors[index], colors[(index + 1) % colors.Length], Smooth(position - index));
    }

    private static LightingFrame Fire(double phase)
    {
        // Layered, integer-frequency waves give organic flicker with a seamless loop.
        // No runtime randomness: preview and game-report hooks always render the same color.
        var angle = phase * Math.PI * 2;
        var heat = 0.5 + 0.25 * Math.Sin(angle * 3 + 0.6) +
            0.15 * Math.Sin(angle * 7 + 1.8) + 0.1 * Math.Sin(angle * 11 + 0.4);
        return Dim(Mix(new(255, 36, 0, 0), new(255, 190, 24, 0), heat), 0.45 + 0.55 * heat);
    }

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
