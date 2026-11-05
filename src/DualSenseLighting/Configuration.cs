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
    public bool HasCompletedSetup { get; set; }
    public bool HasAcknowledgedRecording { get; set; }
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
    public HapticOutputMode HapticOutput { get; set; } = HapticOutputMode.Automatic;
    // -1 selects the first connected XInput slot; 0–3 select a specific controller.
    public int RumbleController { get; set; } = -1;
    public float RumbleStrength { get; set; } = 0.5f;
    public int RumblePulseMilliseconds { get; set; } = RumblePulse.DefaultMilliseconds;
    public float BluetoothMotorBalance { get; set; } = HapticSettings.DefaultBluetoothMotorBalance;
    public bool HapticEnabled { get; set; } = true;
    public bool CombatHaptics { get; set; } = true;
    public bool CharacterHaptics { get; set; } = true;
    public float HapticStrength { get; set; } = 0.25f;
    public bool HapticBossEnabled { get; set; } = true;
    public bool HapticRegularEnemyEnabled { get; set; } = true;
    public float HapticEnemyStrength { get; set; } = 1;
    public bool HapticHitReactions { get; set; }
    public bool HapticScreenEffects { get; set; } = true;
    public float HapticScreenStrength { get; set; } = 0.25f;
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
    public float FootstepMultiplier { get; set; } = 6;

    public bool IsFeatureEnabled(ControllerFeature feature) => feature switch
    {
        ControllerFeature.Lighting => Enabled,
        ControllerFeature.Haptics => HapticEnabled,
        ControllerFeature.CombatHaptics => CombatHaptics,
        ControllerFeature.PlayerSfx => CharacterHaptics,
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
            case ControllerFeature.Haptics: HapticEnabled = enabled; break;
            case ControllerFeature.CombatHaptics: CombatHaptics = enabled; break;
            case ControllerFeature.PlayerSfx: CharacterHaptics = enabled; break;
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
        HapticOutput = defaults.HapticOutput;
        RumbleController = defaults.RumbleController;
        RumbleStrength = defaults.RumbleStrength;
        RumblePulseMilliseconds = defaults.RumblePulseMilliseconds;
        BluetoothMotorBalance = defaults.BluetoothMotorBalance;
        HapticEnabled = defaults.HapticEnabled;
        CombatHaptics = defaults.CombatHaptics;
        CharacterHaptics = defaults.CharacterHaptics;
        HapticStrength = defaults.HapticStrength;
        HapticBossEnabled = defaults.HapticBossEnabled;
        HapticRegularEnemyEnabled = defaults.HapticRegularEnemyEnabled;
        HapticEnemyStrength = defaults.HapticEnemyStrength;
        HapticHitReactions = defaults.HapticHitReactions;
        HapticScreenEffects = defaults.HapticScreenEffects;
        HapticScreenStrength = defaults.HapticScreenStrength;
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

    public void CompleteSetup(bool lighting, bool haptics)
    {
        Enabled = lighting;
        HapticEnabled = haptics;
        HasCompletedSetup = HasSeenGeneralInfo = true;
    }

    public bool CanRecordDiagnostics() => HasCompletedSetup && HasAcknowledgedRecording;

    public HapticSettings HapticsSnapshot(bool requireSetup = false)
    {
        var master = HapticEnabled && (!requireSetup || HasCompletedSetup);
        var combat = master && CombatHaptics;
        var camera = HapticDistanceSource == HapticDistanceOrigin.Camera;
        return new HapticSettings(
            Enabled: combat && CharacterHaptics, Strength: HapticStrength,
            BossEnabled: combat && HapticBossEnabled, RegularEnemyEnabled: combat && HapticRegularEnemyEnabled,
            HitReactions: combat && HapticHitReactions, EnemyStrength: HapticEnemyStrength,
            Spatial: SpatialHaptics, DistanceFalloff: HapticDistanceFalloff,
            NearDistance: camera ? HapticNearDistance : HapticCharacterNearDistance,
            FarDistance: camera ? HapticFarDistance : HapticCharacterFarDistance,
            AutomaticDistance: HapticAutomaticDistance, DistanceOrigin: HapticDistanceSource,
            FollowImpact: HapticFollowImpact, ImpactBias: HapticImpactBias,
            FootstepAmplifier: master && FootstepAmplifier, FootstepMultiplier: FootstepMultiplier,
            ScreenEffects: master && HapticScreenEffects, ScreenStrength: HapticScreenStrength,
            OutputMode: HapticOutput, RumbleController: RumbleController,
            RumbleStrength: RumbleStrength, RumblePulseMilliseconds: RumblePulseMilliseconds,
            BluetoothMotorBalance: BluetoothMotorBalance).Normalize();
    }

    public LightingSettings Snapshot(bool requireSetup = false) => new LightingSettings(Enabled && (!requireSetup || HasCompletedSetup), Effect, Red, Green, Blue,
        Brightness, CycleSeconds, PlayerLights, PlayerAnimation, PlayerCycleSeconds,
        SecondaryRed, SecondaryGreen, SecondaryBlue, SfxSensitivity, SfxNoiseGate, SfxReleaseSeconds, SfxAutomaticGate, AudioSource, PlayerMode).Normalize();
}
