// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

public enum HapticDistanceOrigin { Character, Camera }
public enum EnemySoundKind { None, Boss, Regular }
public enum HapticRoute { None, Native, Original, MissingPosition, MissingListener, SpatialUnavailable, Spatial, DistanceCutoff, NativeFootstep }

public sealed record HapticSettings(bool Enabled = false, float Strength = 0.25f,
    bool Spatial = false, bool DistanceFalloff = true,
    float NearDistance = HapticSettings.RecommendedNearDistance, float FarDistance = HapticSettings.RecommendedFarDistance,
    bool AutomaticDistance = true, HapticDistanceOrigin DistanceOrigin = HapticDistanceOrigin.Character,
    bool FollowImpact = true, float ImpactBias = HapticSettings.MinimumImpactBias,
    bool FootstepAmplifier = false, float FootstepMultiplier = 1.5f,
    bool BossEnabled = false, bool RegularEnemyEnabled = false, float EnemyStrength = 0.25f, bool HitReactions = false,
    HapticOutputMode OutputMode = HapticOutputMode.Automatic, int RumbleController = -1,
    bool ScreenEffects = false, float ScreenStrength = 0.25f,
    float RumbleStrength = 0.5f, int RumblePulseMilliseconds = RumblePulse.DefaultMilliseconds,
    float BluetoothMotorBalance = HapticSettings.DefaultBluetoothMotorBalance)
{
    // Publish the selected backend with effective settings so in-flight audio
    // blocks cannot cross a backend switch. Configuration snapshots leave this off.
    internal bool RegularRumble { get; init; }
    internal bool BluetoothRumble { get; init; }
    public const float MinimumImpactBias = 0.25f;
    public const float DefaultBluetoothMotorBalance = 0.85f;
    public const float RecommendedNearDistance = 3;
    public const float RecommendedFarDistance = 25;
    public const float MaximumFootstepMultiplier = 6;
    public bool EnemiesEnabled => BossEnabled || RegularEnemyEnabled;
    public bool CombatEnabled => Enabled || EnemiesEnabled || HitReactions;
    public bool NeedsAudio => CombatEnabled || FootstepAmplifier || ScreenEffects;

    public HapticSettings Normalize()
    {
        var near = AutomaticDistance ? RecommendedNearDistance :
            float.IsFinite(NearDistance) ? Math.Clamp(NearDistance, 0, 99) : RecommendedNearDistance;
        var far = AutomaticDistance ? RecommendedFarDistance : float.IsFinite(FarDistance)
            ? Math.Clamp(FarDistance, near + 1, 100) : Math.Max(RecommendedFarDistance, near + 1);
        return this with
        {
            OutputMode = Enum.IsDefined(OutputMode) ? OutputMode : HapticOutputMode.Automatic,
            RumbleController = Math.Clamp(RumbleController, -1, 3),
            Strength = float.IsFinite(Strength) ? Math.Clamp(Strength, 0, 1) : 0.25f,
            EnemyStrength = float.IsFinite(EnemyStrength) ? Math.Clamp(EnemyStrength, 0, 1) : 0.25f,
            ScreenStrength = float.IsFinite(ScreenStrength) ? Math.Clamp(ScreenStrength, 0, 1) : 0.25f,
            RumbleStrength = float.IsFinite(RumbleStrength) ? Math.Clamp(RumbleStrength, 0, 1) : 0.5f,
            RumblePulseMilliseconds = Math.Clamp(RumblePulseMilliseconds, RumblePulse.MinimumMilliseconds, RumblePulse.MaximumMilliseconds),
            BluetoothMotorBalance = float.IsFinite(BluetoothMotorBalance) ? Math.Clamp(BluetoothMotorBalance, -1, 1) : DefaultBluetoothMotorBalance,
            NearDistance = near,
            FarDistance = far,
            ImpactBias = float.IsFinite(ImpactBias) ? Math.Clamp(ImpactBias, MinimumImpactBias, 1) : MinimumImpactBias,
            FootstepMultiplier = float.IsFinite(FootstepMultiplier) ? Math.Clamp(FootstepMultiplier, 1, MaximumFootstepMultiplier) : 1.5f,
            DistanceOrigin = DistanceOrigin is HapticDistanceOrigin.Character or HapticDistanceOrigin.Camera
                ? DistanceOrigin : HapticDistanceOrigin.Character,
        };
    }

    public float AdditionalGain(bool playerOwned, byte bus, uint flags, bool alreadyRouted, EnemySoundKind enemy = EnemySoundKind.None,
        bool hitReaction = false, bool arenaEffect = false)
    {
        // Existing vibration sends (bits 1/3), vibration-only clips (bit 2),
        // footsteps, voices and unrelated actors keep their native behavior.
        var arena = arenaEffect && !playerOwned && !hitReaction && enemy == EnemySoundKind.Boss && bus == 5 && (flags & 4) == 0;
        if (!(GameAudioLayout.IsCombatSound(bus, flags) || arena) ||
            (flags & 0xE) != 0 || alreadyRouted) return 0;
        var gain = 0f;
        if (hitReaction)
        {
            if (HitReactions) gain = EnemyStrength;
        }
        else if (playerOwned)
        {
            if (Enabled) gain = Strength;
        }
        else if (enemy == EnemySoundKind.Boss && BossEnabled || enemy == EnemySoundKind.Regular && RegularEnemyEnabled)
            gain = EnemyStrength;
        return float.IsFinite(gain) ? Math.Clamp(gain, 0, 1) : 0;
    }

    public float NativeFootstepExtraGain(byte bus, bool alreadyRouted, float nativeGain)
    {
        // Amplify the game's existing footstep send; never synthesize a new route from audible footsteps.
        if (!FootstepAmplifier || bus != 6 || !alreadyRouted || !float.IsFinite(nativeGain) || nativeGain <= 0 || nativeGain > 4)
            return 0;
        var multiplier = float.IsFinite(FootstepMultiplier) ? Math.Clamp(FootstepMultiplier, 1, MaximumFootstepMultiplier) : 1.5f;
        return nativeGain * (multiplier - 1);
    }
}
