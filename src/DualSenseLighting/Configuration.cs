// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using Dalamud.Configuration;
using DualSenseLighting.Core;
using DualSenseLighting.Ui;

namespace DualSenseLighting;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public UiLanguage Language { get; set; } = UiLanguage.English;
    public bool HasSeenGeneralInfo { get; set; }
    public bool Enabled { get; set; } = true;
    public LightingEffect Effect { get; set; }
    public byte Red { get; set; } = 119;
    public byte Green { get; set; } = 255;
    public byte Blue { get; set; }
    public byte SecondaryRed { get; set; }
    public byte SecondaryGreen { get; set; } = 160;
    public byte SecondaryBlue { get; set; } = 255;
    public float Brightness { get; set; } = 1;
    public float CycleSeconds { get; set; } = 8;
    public byte PlayerLights { get; set; }
    public PlayerIndicatorMode PlayerMode { get; set; } = PlayerIndicatorMode.Animation;
    public PlayerIndicatorAnimation PlayerAnimation { get; set; } = PlayerIndicatorAnimation.Steady;
    public float PlayerCycleSeconds { get; set; } = 2;
    public float SfxSensitivity { get; set; } = 8;
    public float SfxNoiseGate { get; set; } = 0.002f;
    public float SfxReleaseSeconds { get; set; } = 0.18f;
    public bool SfxAutomaticGate { get; set; } = true;
    public SfxSource AudioSource { get; set; } = SfxSource.PlayerOnly;
    public bool CharacterHaptics { get; set; } = true;
    public float HapticStrength { get; set; } = 0.8f;
    public bool SpatialHaptics { get; set; } = true;
    public bool HapticDistanceFalloff { get; set; } = true;
    public HapticDistanceOrigin HapticDistanceSource { get; set; } = HapticDistanceOrigin.Character;
    public bool HapticAutomaticDistance { get; set; } = true;
    // Keep previous camera limits for the Camera option; character limits are independent.
    public float HapticNearDistance { get; set; } = HapticSettings.RecommendedNearDistance;
    public float HapticFarDistance { get; set; } = HapticSettings.RecommendedFarDistance;
    public float HapticCharacterNearDistance { get; set; } = HapticSettings.RecommendedNearDistance;
    public float HapticCharacterFarDistance { get; set; } = HapticSettings.RecommendedFarDistance;
    public bool HapticFollowImpact { get; set; } = true;
    public float HapticImpactBias { get; set; } = 1;
    public bool FootstepAmplifier { get; set; }
    public float FootstepMultiplier { get; set; } = 1.5f;

    public bool IsFeatureEnabled(ControllerFeature feature) => feature switch
    {
        ControllerFeature.Lighting => Enabled,
        ControllerFeature.CombatHaptics => CharacterHaptics,
        ControllerFeature.Footsteps => FootstepAmplifier,
        ControllerFeature.SpatialHaptics => SpatialHaptics,
        ControllerFeature.DistanceFade => HapticDistanceFalloff,
        ControllerFeature.FollowImpact => HapticFollowImpact,
        _ => throw new ArgumentOutOfRangeException(nameof(feature)),
    };

    public bool SetFeature(ControllerFeature feature, FeatureSwitch operation)
    {
        var current = IsFeatureEnabled(feature);
        var enabled = operation switch
        {
            FeatureSwitch.On => true,
            FeatureSwitch.Off => false,
            FeatureSwitch.Toggle => !current,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        if (enabled == current) return false;
        switch (feature)
        {
            case ControllerFeature.Lighting: Enabled = enabled; break;
            case ControllerFeature.CombatHaptics: CharacterHaptics = enabled; break;
            case ControllerFeature.Footsteps: FootstepAmplifier = enabled; break;
            case ControllerFeature.SpatialHaptics: SpatialHaptics = enabled; break;
            case ControllerFeature.DistanceFade: HapticDistanceFalloff = enabled; break;
            case ControllerFeature.FollowImpact: HapticFollowImpact = enabled; break;
        }
        return true;
    }

    public void ResetHapticsToDefaults()
    {
        var defaults = new Configuration();
        CharacterHaptics = defaults.CharacterHaptics;
        HapticStrength = defaults.HapticStrength;
        SpatialHaptics = defaults.SpatialHaptics;
        HapticDistanceFalloff = defaults.HapticDistanceFalloff;
        HapticDistanceSource = defaults.HapticDistanceSource;
        HapticAutomaticDistance = defaults.HapticAutomaticDistance;
        HapticNearDistance = defaults.HapticNearDistance;
        HapticFarDistance = defaults.HapticFarDistance;
        HapticCharacterNearDistance = defaults.HapticCharacterNearDistance;
        HapticCharacterFarDistance = defaults.HapticCharacterFarDistance;
        HapticFollowImpact = defaults.HapticFollowImpact;
        HapticImpactBias = defaults.HapticImpactBias;
        FootstepAmplifier = defaults.FootstepAmplifier;
        FootstepMultiplier = defaults.FootstepMultiplier;
    }

    public void ResetLightingToDefaults()
    {
        var defaults = new Configuration();
        Enabled = defaults.Enabled;
        Effect = defaults.Effect;
        Red = defaults.Red;
        Green = defaults.Green;
        Blue = defaults.Blue;
        SecondaryRed = defaults.SecondaryRed;
        SecondaryGreen = defaults.SecondaryGreen;
        SecondaryBlue = defaults.SecondaryBlue;
        Brightness = defaults.Brightness;
        CycleSeconds = defaults.CycleSeconds;
        PlayerLights = defaults.PlayerLights;
        PlayerMode = defaults.PlayerMode;
        PlayerAnimation = defaults.PlayerAnimation;
        PlayerCycleSeconds = defaults.PlayerCycleSeconds;
        SfxSensitivity = defaults.SfxSensitivity;
        SfxNoiseGate = defaults.SfxNoiseGate;
        SfxReleaseSeconds = defaults.SfxReleaseSeconds;
        SfxAutomaticGate = defaults.SfxAutomaticGate;
        AudioSource = defaults.AudioSource;
    }

    public HapticSettings HapticsSnapshot() => new HapticSettings(CharacterHaptics, HapticStrength,
        SpatialHaptics, HapticDistanceFalloff,
        HapticDistanceSource == HapticDistanceOrigin.Camera ? HapticNearDistance : HapticCharacterNearDistance,
        HapticDistanceSource == HapticDistanceOrigin.Camera ? HapticFarDistance : HapticCharacterFarDistance,
        HapticAutomaticDistance, HapticDistanceSource, HapticFollowImpact, HapticImpactBias,
        FootstepAmplifier, FootstepMultiplier).Normalize();

    public LightingSettings Snapshot() => new LightingSettings(Enabled, Effect, Red, Green, Blue,
        Brightness, CycleSeconds, PlayerLights, PlayerAnimation, PlayerCycleSeconds,
        SecondaryRed, SecondaryGreen, SecondaryBlue, SfxSensitivity, SfxNoiseGate, SfxReleaseSeconds, SfxAutomaticGate, AudioSource, PlayerMode).Normalize();
}
