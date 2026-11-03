// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

public enum HapticDistanceOrigin { Character, Camera }
public enum HapticRoute { None, Native, Original, MissingPosition, MissingListener, SpatialUnavailable, Spatial, DistanceCutoff, NativeFootstep }

public sealed record HapticSettings(bool Enabled = false, float Strength = 0.25f,
    bool Spatial = false, bool DistanceFalloff = true,
    float NearDistance = HapticSettings.RecommendedNearDistance, float FarDistance = HapticSettings.RecommendedFarDistance,
    bool AutomaticDistance = true, HapticDistanceOrigin DistanceOrigin = HapticDistanceOrigin.Character,
    bool FollowImpact = true, float ImpactBias = HapticSettings.MinimumImpactBias,
    bool FootstepAmplifier = false, float FootstepMultiplier = 1.5f)
{
    public const float MinimumImpactBias = 0.25f;
    public const float RecommendedNearDistance = 3;
    public const float RecommendedFarDistance = 25;
    public const float MaximumFootstepMultiplier = 6;
    public bool NeedsAudio => Enabled || FootstepAmplifier;

    public HapticSettings Normalize()
    {
        var near = AutomaticDistance ? RecommendedNearDistance :
            float.IsFinite(NearDistance) ? Math.Clamp(NearDistance, 0, 99) : RecommendedNearDistance;
        var far = AutomaticDistance ? RecommendedFarDistance : float.IsFinite(FarDistance)
            ? Math.Clamp(FarDistance, near + 1, 100) : Math.Max(RecommendedFarDistance, near + 1);
        return this with
        {
            Strength = float.IsFinite(Strength) ? Math.Clamp(Strength, 0, 1) : 0.25f,
            NearDistance = near,
            FarDistance = far,
            ImpactBias = float.IsFinite(ImpactBias) ? Math.Clamp(ImpactBias, MinimumImpactBias, 1) : MinimumImpactBias,
            FootstepMultiplier = float.IsFinite(FootstepMultiplier) ? Math.Clamp(FootstepMultiplier, 1, MaximumFootstepMultiplier) : 1.5f,
            DistanceOrigin = DistanceOrigin is HapticDistanceOrigin.Character or HapticDistanceOrigin.Camera
                ? DistanceOrigin : HapticDistanceOrigin.Character,
        };
    }

    public float AdditionalGain(bool playerOwned, byte bus, uint flags, bool alreadyRouted)
    {
        // Existing vibration sends (bits 1/3), vibration-only clips (bit 2),
        // footsteps, voices and unrelated actors keep their native behavior.
        if (!Enabled || !playerOwned || !GameAudioLayout.IsCombatSound(bus, flags) ||
            (flags & 0xE) != 0 || alreadyRouted) return 0;
        return float.IsFinite(Strength) ? Math.Clamp(Strength, 0, 1) : 0;
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
