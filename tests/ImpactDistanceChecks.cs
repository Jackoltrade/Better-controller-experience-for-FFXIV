// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static unsafe class ImpactDistanceChecks
{
    private static HapticSettings Settings => new HapticSettings(BossEnabled: true, Spatial: true,
        DistanceFalloff: true, NearDistance: 3, FarDistance: 25, AutomaticDistance: false,
        DistanceOrigin: HapticDistanceOrigin.Character).Normalize();
    private static HapticImpactPosition Impact(Vector3 point, bool attached = true) =>
        new(point, 1234, 5678, HapticImpactSource.AreaTarget, attached);

    internal static void Run(Action<bool, string> check)
    {
        CheckGeometry(check);
        CheckMetadata(check);
        CheckSoundPolicy(check);
        CheckUnlocatedExecution(check);
        CheckSelfTargetedTimeline(check);
        CheckTargetSelection(check);
        CheckOwnership(check);
        CheckNativePreservation(check);
        CheckAllocations(check);
    }

    private static void CheckGeometry(Action<bool, string> check)
    {
        var now = Stopwatch.GetTimestamp();
        var pose = new HapticListenerPose(Vector3.Zero, Vector3.UnitX, now, new(0, 0, 60));
        var settings = Settings;
        var emitter = new Vector3(90, 0, 0);
        var point = Impact(new(0, 0, 3));
        var native = HapticSpatializer.Evaluate(settings, pose, emitter, now);
        var impact = HapticSpatializer.Evaluate(settings, pose, emitter, now, point.DistancePosition(emitter));
        check(native.Valid && native.Distance == 90 && native.Left == 0 && native.Right == 0 &&
            impact.Valid && impact.Distance == 3 && impact.Right > 0 && impact.Pan == native.Pan,
            "A nearby impact failed to rescue a distant attached emitter or changed its native bearing");
        foreach (var distance in new[] { 25f, 26f })
        {
            var frame = HapticSpatializer.Evaluate(settings, pose, new(2, 0, 0), now,
                Impact(new(0, 0, distance)).DistancePosition(new(2, 0, 0)));
            check(frame.Valid && frame.Distance == distance && frame.Left == 0 && frame.Right == 0,
                "A near attached emitter bypassed the impact's exact/far distance cutoff");
        }
        var independent = Impact(new(0, 0, 90), attached: false);
        var preserved = HapticSpatializer.Evaluate(settings, pose, new(2, 0, 0), now,
            independent.DistancePosition(new(2, 0, 0)));
        check(preserved.Distance == 2 && preserved.Right > 0,
            "An unrelated action target replaced an actual independent VFX position");
        var camera = HapticSpatializer.Evaluate(settings with { DistanceOrigin = HapticDistanceOrigin.Camera },
            pose, emitter, now, new Vector3(0, 0, 57));
        check(camera.Distance == 3 && camera.Pan == impact.Pan && camera.Right > 0,
            "Impact distance used the character instead of the selected camera origin or repanned the emitter");
        var invalid = HapticSpatializer.Evaluate(settings, pose, new(2, 0, 0), now, new Vector3(float.NaN, 0, 0));
        check(invalid.Valid && invalid.Distance == 2 && invalid.Right > 0,
            "A nonfinite optional impact distance corrupted the valid native-position fallback");
    }

    private static void CheckMetadata(Action<bool, string> check)
    {
        var point = Impact(new(3, 0, 0));
        check(point.IsValid && point.DistancePosition(new(90, 0, 0)) == point.Position,
            "Valid attached impact metadata did not select the impact distance point");
        var detached = point with { CasterAttached = false };
        check(detached.DistancePosition(new(90, 0, 0)) == null && detached.DistancePosition(null) == point.Position,
            "Independent native position precedence or missing-position fallback changed");
        foreach (var invalid in new[] { point with { ActionId = 0 }, point with { Sequence = 0 },
            point with { Source = HapticImpactSource.None }, point with { Source = (HapticImpactSource)999 },
            point with { Position = new(float.PositiveInfinity, 0, 0) }, point with { Position = new(1_000_000, 0, 0) } })
            check(!invalid.IsValid && invalid.DistancePosition(null) == null,
                "Incomplete, nonfinite or unsupported impact evidence became a distance override");
        foreach (var source in new[] { HapticImpactSource.AreaTarget, HapticImpactSource.AnimationTarget, HapticImpactSource.SingleTarget })
            check((point with { Source = source }).IsValid, "A supported impact provenance was rejected");
    }

    private static void CheckSoundPolicy(Action<bool, string> check)
    {
        var point = Impact(new(0, 0, 3));
        var matched = new EnemyActionTag(EnemyActionKind.Skill, ActionId: point.ActionId, TimelineId: 3209,
            Phase: EnemyActionPhase.Execution, Binding: EnemySoundBinding.Target);
        check(point.ForSound(matched with { ActionId = point.ActionId + 1 }, 3, true) == null &&
            point.ForSound(new EnemyActionTag(EnemyActionKind.Unknown), 3, false) == null,
            "A conflicting action or unlinked unknown timeline borrowed another cast's impact");
        var avfx = point.ForSound(matched, 1, false);
        check(avfx is { CasterAttached: false } && avfx.Value.DistancePosition(new(90, 0, 0)) == null &&
            avfx.Value.ActionId == point.ActionId && avfx.Value.Sequence == point.Sequence,
            "Explicit action-linked independent AVFX lost its native-position precedence");
        var root = point.ForSound(matched with { ActionId = 0, Binding = EnemySoundBinding.Caster }, 3, true);
        check(root is { CasterAttached: true } && root.Value.DistancePosition(new(90, 0, 0)) == point.Position,
            "An explicitly linked execution root did not admit its caster-attached impact");
        var casterOrigin = point.ForSound(matched with { ActionId = 0, Binding = EnemySoundBinding.CasterOrigin }, 3, true);
        check(casterOrigin is { CasterAttached: true }, "Linked execution caster-origin sound lost its attachment policy");
        check(point.ForSound(matched with { ActionId = 0, Phase = EnemyActionPhase.Caster,
                Binding = EnemySoundBinding.Caster }, 3, false) == null,
            "A casting timeline without an execution/action link inherited impact distance");
        check(point.ForSound(matched with { Phase = EnemyActionPhase.Caster, Binding = EnemySoundBinding.Caster },
            3, false) == null,
            "A matching action ID overrode the retained cast location without an execution link");
        var target = point.ForSound(matched, 3, true);
        var ordinary = point.ForSound(matched with { Binding = EnemySoundBinding.Caster }, 3, false);
        check(target is { CasterAttached: false } && ordinary is { CasterAttached: false },
            "Target-positioned or non-execution sound was incorrectly relabeled caster-attached");
        foreach (var kind in new[] { EnemyActionKind.AutoAttack, EnemyActionKind.HitReaction })
            check(point.ForSound(matched with { Kind = kind }, 3, true) == null,
                "Basic/hit-reaction audio inherited a skill's impact override");
        check(point.ForSound(matched, 0, true) == null && point.ForSound(matched, 2, true) == null &&
            (point with { Sequence = 0 }).ForSound(matched, 3, true) == null,
            "Unsupported sound origin or invalid impact metadata bypassed the exact-action policy");
        var execution = matched with { Phase = EnemyActionPhase.Execution };
        check(EnemySkillCatalog.CanUseAnimationFallback(execution, EnemySoundBinding.Caster) &&
            EnemySkillCatalog.CanUseAnimationFallback(execution, EnemySoundBinding.CasterOrigin),
            "Confirmed execution skill lost its caster/caster-origin fallback eligibility");
        foreach (var phase in new[] { EnemyActionPhase.Caster, EnemyActionPhase.ReleasedEffect, EnemyActionPhase.Unknown })
            check(!EnemySkillCatalog.CanUseAnimationFallback(execution with { Phase = phase }, EnemySoundBinding.Caster),
                "Non-execution timeline inherited animation fallback eligibility");
        foreach (var kind in new[] { EnemyActionKind.Unknown, EnemyActionKind.AutoAttack, EnemyActionKind.HitReaction })
            check(!EnemySkillCatalog.CanUseAnimationFallback(execution with { Kind = kind }, EnemySoundBinding.Caster),
                "Unknown/basic/hit audio inherited skill execution fallback eligibility");
        check(!EnemySkillCatalog.CanUseAnimationFallback(execution, EnemySoundBinding.Target) &&
            !EnemySkillCatalog.CanUseAnimationFallback(execution, EnemySoundBinding.Unknown) &&
            !EnemySkillCatalog.CanUseAnimationFallback(execution, EnemySoundBinding.Default),
            "A non-caster binding was admitted through animation fallback");
    }

    private static void CheckUnlocatedExecution(Action<bool, string> check)
    {
        var now = Stopwatch.GetTimestamp(); var pose = new HapticListenerPose(Vector3.Zero, Vector3.UnitX, now);
        var settings = Settings; var emitter = new Vector3(90, 0, 0);
        var ordinary = HapticSpatializer.Evaluate(settings, pose, emitter, now);
        var fallback = HapticSpatializer.Evaluate(settings, pose, emitter, now, allowUnlocatedExecution: true);
        check(ordinary.Valid && ordinary.Left == 0 && ordinary.Right == 0 && fallback.Valid &&
            fallback.Distance == 90 && fallback.Pan == ordinary.Pan && fallback.Right == 1 && fallback.Left == 0,
            "Explicit unlocated execution did not retain native distance/bearing while bypassing only distance fade");
        var near = HapticSpatializer.Evaluate(settings, pose, emitter, now, new Vector3(0, 0, 3), true);
        var far = HapticSpatializer.Evaluate(settings, pose, emitter, now, new Vector3(0, 0, 25), true);
        check(near.Distance == 3 && near.Right == 1 && far.Distance == 25 && far.Right == 0 && far.Left == 0,
            "Unlocated-execution permission overrode a valid known impact's normal distance/cutoff");
    }

    private static void CheckSelfTargetedTimeline(Action<bool, string> check)
    {
        var bytes = new byte[0x198];
        void Set(int offset, int value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        Set(0x18C, 7); Set(0x190, 1); Set(0x194, 7);
        check(GameAudioLayout.IsSelfTargetedTimeline(bytes, 7), "A single exact owner-targeted timeline was rejected");
        check(!GameAudioLayout.IsSelfTargetedTimeline(bytes.AsSpan(0, 0x197), 7) &&
            !GameAudioLayout.IsSelfTargetedTimeline(bytes, -1) && !GameAudioLayout.IsSelfTargetedTimeline(bytes, 8),
            "Short timeline/invalid owner/owner mismatch became execution fallback evidence");
        foreach (var count in new[] { -1, 0, 2, int.MaxValue })
        {
            Set(0x190, count);
            check(!GameAudioLayout.IsSelfTargetedTimeline(bytes, 7), "A non-single target count became self-targeted execution");
        }
        Set(0x190, 1); Set(0x194, 8);
        check(!GameAudioLayout.IsSelfTargetedTimeline(bytes, 7), "A different timeline target was treated as its owner");
        Set(0x194, 7); Set(0x18C, 8);
        check(!GameAudioLayout.IsSelfTargetedTimeline(bytes, 7), "The timeline's embedded owner mismatch was ignored");
    }

    private static void CheckTargetSelection(Action<bool, string> check)
    {
        var reads = 0;
        Vector3? Resolve(ulong id) { reads++; return id == 22 ? new Vector3(3, 0, 0) : null; }
        var confirmed = new ulong[] { 11, 22 };
        var area = HapticImpactPosition.FromAction(1234, 5678, true, new(0, 0, 3), 0,
            Array.Empty<ulong>(), Resolve);
        check(area is { Source: HapticImpactSource.AreaTarget, ActionId: 1234, Sequence: 5678 } &&
            area.Value.Position == new Vector3(0, 0, 3) && reads == 0,
            "An explicit area target was lost or unnecessarily resolved actor positions");
        check(HapticImpactPosition.FromAction(1234, 5678, false, new(0, 0, 3), 0,
            Array.Empty<ulong>(), Resolve) == null && reads == 0,
            "An unflagged native area-position field became semantic impact evidence");
        var primary = HapticImpactPosition.FromAction(1234, 5678, false, null, 22, confirmed, Resolve);
        check(primary is { Source: HapticImpactSource.AnimationTarget } && primary.Value.Position == new Vector3(3, 0, 0) && reads == 1,
            "Confirmed animation target did not select the exact action's primary impact");
        check(HapticImpactPosition.FromAction(1234, 5678, false, null, 33, confirmed, Resolve) == null && reads == 1,
            "Ambiguous targets selected an unrelated primary or arbitrary nearest actor");
        var single = HapticImpactPosition.FromAction(1234, 5678, false, null, 0, new ulong[] { 22 }, Resolve);
        check(single is { Source: HapticImpactSource.SingleTarget } && single.Value.Position == new Vector3(3, 0, 0) && reads == 2,
            "A sole confirmed target did not provide the bounded fallback");
        check(HapticImpactPosition.FromAction(0, 5678, false, null, 22, confirmed, Resolve) == null &&
            HapticImpactPosition.FromAction(1234, 0, false, null, 22, confirmed, Resolve) == null && reads == 2,
            "Missing action identity/sequence resolved targets or created impact evidence");
        check(HapticImpactPosition.FromAction(1234, 5678, false, null, 22, confirmed,
            _ => new Vector3(float.NaN, 0, 0)) == null,
            "Nonfinite target resolution became an action distance override");
        var readsBeforeSelf = reads;
        check(HapticImpactPosition.FromAction(1234, 5678, false, null, 22, confirmed, Resolve,
                sourceEntityId: 22) == null &&
            HapticImpactPosition.FromAction(1234, 5678, false, null, 0, new ulong[] { 22 }, Resolve,
                sourceEntityId: 22) == null && reads == readsBeforeSelf,
            "An explicit primary/sole self target was mistaken for a separately located impact");
        var selfArea = HapticImpactPosition.FromAction(1234, 5678, true, new(0, 0, 3), 22, new ulong[] { 22 },
            Resolve, sourceEntityId: 22);
        check(selfArea is { Source: HapticImpactSource.AreaTarget } && selfArea.Value.Position == new Vector3(0, 0, 3),
            "Rejecting self-target actor lookup also discarded an explicit area contact position");
    }

    private static GameAudioLayout.PlayerSoundOwnership Ownership(GameAudioLayout.SoundData* pool, Func<nint, bool?>? reader = null)
    {
        var ownership = new GameAudioLayout.PlayerSoundOwnership(pool, reader);
        ownership.SetPlayer(100, 1000, 10000);
        ownership.SetEnemies(new[] { new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss) }, 1);
        pool->IsActive = 1; pool->SoundId = 123; pool->ResourceHandle = 999; pool->SoundNumber = 0;
        pool->IsPositional = 1; pool->Position = new(90, 0, 0);
        return ownership;
    }
    private static EnemyActionTag Released => new(EnemyActionKind.Skill, TimelineId: 3209,
        Phase: EnemyActionPhase.ReleasedEffect, HasAttackType: true);

    private static void CheckOwnership(Action<bool, string> check)
    {
        var pool = (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed(
            (nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        try
        {
            var ownership = Ownership(pool); var caster = ownership.ResolveCaster(200, 0); var point = Impact(new(0, 0, 3));
            ownership.Acquired(pool, caster, action: Released, impact: point, selfTargetedExecution: true);
            var read = ownership.Inspect(123, true, true);
            check(read.Enemy == EnemySoundKind.Boss && read.Position == pool->Position && read.Impact == point && read.SelfTargetedExecution &&
                ownership.Snapshot()[0].Impact == point && ownership.Snapshot()[0].SelfTargetedExecution,
                "Impact attribution was not copied with the same accepted native reservation");
            ownership.Acquired(pool, caster, action: Released);
            check(ownership.Inspect(123, true, true).Impact == null && !ownership.Inspect(123, true, true).SelfTargetedExecution,
                "Slot reuse without impact evidence retained a previous action's target");
            ownership.Acquired(pool, caster, action: Released, impact: point with { Sequence = 0 });
            check(ownership.Inspect(123, true, true).Impact == null, "An invalid impact entered the ownership publication");
            ownership.Acquired(pool, caster, action: Released, impact: point, selfTargetedExecution: true);
            ownership.SetEnemies(new[] { new GameAudioLayout.EnemyActor(200, 2001, 20001, 20, EnemySoundKind.Boss) }, 1);
            var replaced = ownership.Inspect(123, true, true);
            check(replaced.Enemy == EnemySoundKind.None && replaced.Impact == null && !replaced.SelfTargetedExecution,
                "An actor replaced at the same address inherited old sound/impact evidence");
            ownership.Acquired(pool, null, action: Released, impact: point);
            check(ownership.Inspect(123, true, true).Impact == null, "Unowned acquisition leaked a target into later audio routing");
            GameAudioLayout.PlayerSoundOwnership? raced = null;
            var replaceDuringRead = false;
            var nativeAddress = (nint)pool;
            var later = point with { Sequence = point.Sequence + 1, Position = new(0, 0, 20) };
            raced = Ownership(pool, _ =>
            {
                if (replaceDuringRead)
                {
                    replaceDuringRead = false;
                    raced!.Acquired((GameAudioLayout.SoundData*)nativeAddress, raced.ResolveCaster(200, 0),
                        action: Released, impact: later);
                }
                return false;
            });
            raced.Acquired(pool, raced.ResolveCaster(200, 0), action: Released, impact: point);
            replaceDuringRead = true;
            check(raced.Inspect(123, true, true).Impact == null && raced.Inspect(123, true, true).Impact == later,
                "A reservation replaced during native-name classification published a cross-generation impact");
        }
        finally { NativeMemory.Free(pool); }
    }

    private static void CheckNativePreservation(Action<bool, string> check)
    {
        var table = stackalloc nint[16]; new Span<nint>(table, 16).Clear(); table[15] = 1234;
        var destination = stackalloc byte[0x160]; new Span<byte>(destination, 0x160).Clear();
        *(nint*)destination = (nint)table; *(int*)(destination + 0xE0) = 1;
        "Vibration\0"u8.CopyTo(new Span<byte>(destination + 0x13E, 10));
        var graph = stackalloc nint[15]; new Span<nint>(graph, 15).Clear(); graph[9] = (nint)destination;
        var output = new NativeHapticOutput(graph, 1234, (nint)table, (nint)table + 16 * 8);
        var bed = new float[16]; bed[0] = bed[8] = 1;
        var ambisonic = new float[32]; ambisonic[0] = ambisonic[16] = 1;
        output.SetSpatialMatrices(bed, ambisonic); output.SetEnemyContext(true); output.Configure(Settings, true, true);
        output.SetListener(new(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp()));
        GameAudioLayout.SourceVoice voice = default; voice.SendCount = 1;
        var sends = (GameAudioLayout.VoiceSend*)((byte*)&voice + 0x940); sends[0].Destination = 123; sends[0].Gain = 1;
        var original = new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice)).ToArray();
        var near = output.Prepare(&voice, false, 2, 0, new(90, 0, 0), enemy: EnemySoundKind.Boss,
            distancePosition: new Vector3(0, 0, 3));
        check(near.HasOutput && near.Route == HapticRoute.Spatial && near.Spatial.Distance == 3,
            "Native preparation suppressed a nearby impact because its attached emitter was far away");
        var far = output.Prepare(&voice, false, 2, 0, new(2, 0, 0), enemy: EnemySoundKind.Boss,
            distancePosition: new Vector3(0, 0, 25));
        check(!far.HasOutput && far.Route == HapticRoute.DistanceCutoff,
            "Impact distance failed to retain the output path's hard cutoff");
        var animation = output.Prepare(&voice, false, 2, 0, new(90, 0, 0), enemy: EnemySoundKind.Boss,
            allowUnlocatedExecution: true);
        check(animation.HasOutput && animation.Route == HapticRoute.Animation && animation.Spatial.Distance == 90 &&
            animation.Spatial.Right == 1, "Explicit unlocated execution failed to prepare the full animation route");
        var knownFar = output.Prepare(&voice, false, 2, 0, new(90, 0, 0), enemy: EnemySoundKind.Boss,
            distancePosition: new Vector3(0, 0, 25), allowUnlocatedExecution: true);
        check(!knownFar.HasOutput && knownFar.Route == HapticRoute.DistanceCutoff,
            "Animation fallback bypassed a known far impact's cutoff");
        var knownNear = output.Prepare(&voice, false, 2, 0, new(90, 0, 0), enemy: EnemySoundKind.Boss,
            distancePosition: new Vector3(0, 0, 3), allowUnlocatedExecution: true);
        check(knownNear.Route == HapticRoute.Spatial && knownNear.Spatial.Distance == 3,
            "A known nearby impact was mislabeled as unlocated animation fallback");
        check(!output.Prepare(&voice, false, 5, 0, new(90, 0, 0), enemy: EnemySoundKind.Boss,
            allowUnlocatedExecution: true).HasOutput,
            "Unlocated-execution permission bypassed combat-bus admission");
        output.SetListener(null);
        check(output.Prepare(&voice, false, 2, 0, new(90, 0, 0), enemy: EnemySoundKind.Boss,
            allowUnlocatedExecution: true).Route == HapticRoute.MissingListener,
            "Unlocated-execution permission bypassed the missing listener guard");
        output.SetListener(new(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp()));
        var ordinaryFar = output.Prepare(&voice, false, 2, 0, new(90, 0, 0), enemy: EnemySoundKind.Boss);
        check(!ordinaryFar.HasOutput && ordinaryFar.Route == HapticRoute.DistanceCutoff,
            "Unflagged distant audio inherited animation fallback");
        var unlocated = output.Prepare(&voice, false, 2, 0, position: null, enemy: EnemySoundKind.Boss,
            allowUnlocatedExecution: true);
        check(!unlocated.HasOutput && unlocated.Route == HapticRoute.MissingPosition,
            "Unlocated-execution permission invented a missing native position");
        var fallback = output.Prepare(&voice, false, 2, 0, position: null, enemy: EnemySoundKind.Boss,
            distancePosition: new Vector3(0, 0, 3));
        check(fallback.HasOutput && fallback.Route == HapticRoute.Spatial && fallback.Spatial.Distance == 3 &&
            fallback.Source == new Vector3(0, 0, 3),
            "A missing emitter failed to use the validated nearby impact as its spatial fallback");
        var missing = output.Prepare(&voice, false, 2, 0, position: null, enemy: EnemySoundKind.Boss);
        check(!missing.HasOutput && missing.Route == HapticRoute.MissingPosition,
            "Missing emitter and impact evidence bypassed spatial suppression");
        check(new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice)).SequenceEqual(original),
            "Impact-aware preparation modified the native voice or speaker sends");
        sends[0].Destination = (nint)destination;
        original = new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice)).ToArray();
        var native = output.Prepare(&voice, false, 2, 0, new(90, 0, 0), enemy: EnemySoundKind.Boss,
            distancePosition: new Vector3(0, 0, 3), allowUnlocatedExecution: true);
        check(native.Route == HapticRoute.Native && !native.HasOutput &&
            new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice)).SequenceEqual(original),
            "Impact distance duplicated or modified an existing native vibration send");
    }

    private static void CheckAllocations(Action<bool, string> check)
    {
        var now = Stopwatch.GetTimestamp(); var pose = new HapticListenerPose(Vector3.Zero, Vector3.UnitX, now);
        var settings = Settings; var point = Impact(new(0, 0, 3)); var emitter = new Vector3(90, 0, 0);
        var pool = (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed(
            (nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        try
        {
            var ownership = Ownership(pool); var caster = ownership.ResolveCaster(200, 0);
            ownership.Acquired(pool, caster, action: Released, impact: point);
            pool[1] = pool[0]; pool[1].SoundId = 124;
            ownership.Acquired(pool + 1, caster, action: Released, selfTargetedExecution: true);
            HapticSpatialFrame last = default, animationLast = default;
            Action callback = () =>
            {
                var read = ownership.Inspect(123, true, true);
                last = HapticSpatializer.Evaluate(settings, pose, emitter, now, read.Impact?.DistancePosition(read.Position),
                    read.SelfTargetedExecution);
                var unlocated = ownership.Inspect(124, true, true);
                animationLast = HapticSpatializer.Evaluate(settings, pose, emitter, now, null, unlocated.SelfTargetedExecution);
            };
            var bytes = AllocationChecks.MeasureAllocations(callback, warmup: 1000, iterations: 10000);
            check(bytes == 0 && last.Valid && last.Distance == 3 && animationLast.Distance == 90 && animationLast.Right == 1,
                $"Impact-aware ownership/distance evaluation allocated {bytes} bytes or lost the nearby impact");
            var timer = Stopwatch.StartNew();
            for (var i = 0; i < 10000; i++) callback();
            timer.Stop();
            Console.WriteLine($"Impact+unlocated execution (two ownership/distance evaluations): {timer.Elapsed.TotalMicroseconds / 10000:F3} us/two-operation iteration, {bytes} warmed bytes; advisory combined cost, not incremental cost or game FPS.");
        }
        finally { NativeMemory.Free(pool); }
    }
}
