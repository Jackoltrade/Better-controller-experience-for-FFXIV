// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

// Append effects so saved Solid/Rainbow/Breathing selections retain their numeric values.
public enum LightingEffect { Solid, Rainbow, Breathing, ColorShift, Aurora, Fire, Heartbeat, Comet, RainbowPulse, SfxLoudness, SfxColorBlend }
public enum SfxSource { PlayerOnly, AllCombat }

public sealed record LightingSettings(
    bool Enabled, LightingEffect Effect, byte Red, byte Green, byte Blue,
    float Brightness, float CycleSeconds, byte PlayerLights,
    PlayerIndicatorAnimation PlayerAnimation = PlayerIndicatorAnimation.Steady, float PlayerCycleSeconds = 2,
    byte SecondaryRed = 0, byte SecondaryGreen = 160, byte SecondaryBlue = 255,
    float SfxSensitivity = 8, float SfxNoiseGate = 0.002f, float SfxReleaseSeconds = 0.18f, bool SfxAutomaticGate = true,
    SfxSource AudioSource = SfxSource.PlayerOnly, PlayerIndicatorMode PlayerMode = PlayerIndicatorMode.Animation)
{
    public static LightingSettings Default { get; } = new(true, LightingEffect.Solid, 119, 255, 0, 1, 8, 0);

    public LightingSettings Normalize()
    {
        // Published settings are immutable and already valid. Reuse them in the
        // worker, preview and report hooks instead of cloning on every frame.
        if (Enum.IsDefined(Effect) && Brightness is >= 0 and <= 1 && CycleSeconds is >= 1 and <= 60 &&
            PlayerLights <= 31 && Enum.IsDefined(PlayerAnimation) && PlayerCycleSeconds is >= 1 and <= 12 &&
            SfxSensitivity is >= 1 and <= 32 && SfxNoiseGate is >= 0 and <= 0.05f &&
            SfxReleaseSeconds is >= 0.05f and <= 1 && Enum.IsDefined(AudioSource) && Enum.IsDefined(PlayerMode))
            return this;
        return this with
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
            AudioSource = Enum.IsDefined(AudioSource) ? AudioSource : SfxSource.PlayerOnly,
            PlayerMode = Enum.IsDefined(PlayerMode) ? PlayerMode : PlayerIndicatorMode.Animation,
        };
    }
}

public readonly record struct LightingFrame(byte Red, byte Green, byte Blue, byte PlayerLights, bool PlayerLightsImmediate = false);
