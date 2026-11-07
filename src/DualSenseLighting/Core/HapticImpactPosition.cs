// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Numerics;

namespace DualSenseLighting.Core;

public enum HapticImpactSource { None, AreaTarget, AnimationTarget, SingleTarget }

/// <summary>A copied position from the exact action that created a sound.</summary>
public readonly record struct HapticImpactPosition(Vector3 Position, uint ActionId, uint Sequence,
    HapticImpactSource Source, bool CasterAttached = false)
{
    public bool IsValid => ActionId != 0 && Sequence != 0 &&
        (Source is HapticImpactSource.AreaTarget or HapticImpactSource.AnimationTarget or HapticImpactSource.SingleTarget) &&
        HapticSpatializer.ValidPosition(Position);

    // Independently positioned effects keep their real emitter. Only an explicit
    // caster attachment (or a missing emitter) needs the action's impact fallback.
    public Vector3? DistancePosition(Vector3? nativePosition) =>
        IsValid && (CasterAttached || !nativePosition.HasValue) ? Position : null;

    public HapticImpactPosition? ForSound(EnemyActionTag timeline, byte origin, bool executionTimeline)
    {
        if (!IsValid || origin is not (1 or 3) || timeline.IsHitReaction || timeline.Kind == EnemyActionKind.AutoAttack ||
            (timeline.ActionId != 0 && timeline.ActionId != ActionId) ||
            (timeline.ActionId != ActionId && !executionTimeline) ||
            (timeline.Phase == EnemyActionPhase.Caster && !executionTimeline)) return null;
        return this with { CasterAttached = origin == 3 && executionTimeline &&
            timeline.Binding is EnemySoundBinding.Caster or EnemySoundBinding.CasterOrigin };
    }

    public static HapticImpactPosition? FromAction(uint actionId, uint sequence, bool areaTarget,
        Vector3? areaPosition, ulong animationTarget, ReadOnlySpan<ulong> targets,
        Func<ulong, Vector3?> targetPosition, uint sourceEntityId = 0)
    {
        if (actionId == 0 || sequence == 0 || targets.Length > 32) return null;
        if (areaTarget && areaPosition is { } area && HapticSpatializer.ValidPosition(area))
            return new(area, actionId, sequence, HapticImpactSource.AreaTarget);
        // A multi-target result has no unique contact point. Use its explicit
        // animation target only if the same result confirms that target.
        var primary = animationTarget is not (0 or 0xE0000000) && targets.Contains(animationTarget);
        var target = primary ? animationTarget : targets.Length == 1 ? targets[0] : 0;
        if (target is 0 or 0xE0000000 || sourceEntityId != 0 && (uint)target == sourceEntityId) return null;
        var position = targetPosition(target);
        return position is { } point && HapticSpatializer.ValidPosition(point)
            ? new(point, actionId, sequence, primary ? HapticImpactSource.AnimationTarget : HapticImpactSource.SingleTarget)
            : null;
    }
}
