// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;

internal static unsafe class BossNpcSoundChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckSelection(check);
        CheckNpcSounds(check);
        CheckOwnershipLifetime(check);
        CheckLegacyFilters(check);
        CheckAudioScope(check);
    }

    private static GameAudioLayout.EnemyActor Npc => new(200, 2000, 20000, 20,
        EnemySoundKind.Boss, EnemyActorRole.BossEncounter, 6385, 7715);
    private static EnemyActionTag Released => new(EnemyActionKind.Skill, TimelineId: 3209,
        Phase: EnemyActionPhase.ReleasedEffect);

    private sealed class Fixture : IDisposable
    {
        internal readonly GameAudioLayout.SoundData* Pool = (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed(
            (nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        internal readonly GameAudioLayout.PlayerSoundOwnership Ownership;

        internal Fixture(Func<nint, bool?>? cue = null)
        {
            Ownership = new(Pool, cue);
            Ownership.SetPlayer(100, 1000, 10000);
            Ownership.SetEnemies(new[] { Npc }, 1);
        }

        internal void Reserve(GameAudioLayout.PlayerIdentity? caster, EnemyActionTag tag = default, byte origin = 1)
        {
            Ownership.Acquired(Pool, caster, origin, caster?.Actor ?? 0, timelineOwnerIndex: 20, action: tag);
            Pool->IsActive = 1; Pool->SoundId = -20; Pool->ResourceHandle = 999;
            Pool->SoundNumber = 0; Pool->IsPositional = 1; Pool->Position = new(3, 0, 4);
            Ownership.LinkPlayback(-20, 500);
        }

        public void Dispose() => NativeMemory.Free(Pool);
    }

    private static void CheckSelection(Action<bool, string> check)
    {
        var selected = new GameAudioLayout.EnemyActor[2];
        check(GameAudioLayout.SelectEnemyActors(new[] { Npc }, selected) == 1 && selected[0] == Npc,
            "A caller-validated boss encounter NPC still required legacy skill/helper association");
        foreach (var invalid in new[] { Npc with { Actor = 0 }, Npc with { EntityId = 0 },
            Npc with { EntityId = 0xE0000000 }, Npc with { Kind = EnemySoundKind.None },
            Npc with { Kind = EnemySoundKind.Regular } })
            check(GameAudioLayout.SelectEnemyActors(new[] { invalid }, selected) == 0,
                "Boss encounter selection bypassed actor/entity/category validation");

        var root = Npc with { Actor = 300, EntityId = 3000, ObjectIndex = 30, Role = EnemyActorRole.Combatant };
        var child = Npc with { OwnerId = root.EntityId };
        check(GameAudioLayout.SelectEnemyActors(new[] { root, child }, selected) == 2 && selected[1] == child,
            "Legacy native-owner promotion removed the caller's boss encounter policy from a summon");
        check(GameAudioLayout.SelectEnemyActors(new[] { root, child with { Kind = EnemySoundKind.Regular } }, selected) == 1,
            "A wrong-kind boss encounter role became eligible through legacy summon promotion");
    }

    private static void CheckNpcSounds(Action<bool, string> check)
    {
        var reads = 0;
        using var fixture = new Fixture(_ => { reads++; return false; });
        var ownership = fixture.Ownership;
        var npc = ownership.ResolveTimelineCaster(20, 0);
        check(npc != null && npc.Actor == Npc.Actor, "The broad NPC policy lost exact timeline-owner resolution");
        foreach (var origin in new byte[] { 1, 2, 3 })
            foreach (var tag in new[] { default(EnemyActionTag), new EnemyActionTag(EnemyActionKind.AutoAttack, 7),
                new EnemyActionTag(EnemyActionKind.Skill, TimelineId: 130, Phase: EnemyActionPhase.Caster),
                new EnemyActionTag(EnemyActionKind.Skill, TimelineId: 4573, Phase: EnemyActionPhase.Execution) })
            {
                fixture.Reserve(npc, tag, origin);
                check(ownership.EnemyKindOf(500) == EnemySoundKind.Boss && !ownership.Contains(500) &&
                    ownership.TryGetEnemyPosition(500, out var position) && position == new Vector3(3, 0, 4),
                    "An exact NPC auto/caster/unknown/body SFX failed the boss route or its position lookup");
                var reservation = ownership.Snapshot()[0];
                check(reservation.Action == tag && reservation.Origin == origin && reservation.TimelineOwnerIndex == 20,
                    "Broad NPC routing fabricated an action tag or discarded its source provenance");
            }
        check(reads == 12, "Real NPC effects were not classified once per reservation or repeated cached filename reads");
        fixture.Pool->IsPositional = 0;
        check(ownership.EnemyKindOf(500) == EnemySoundKind.Boss && !ownership.TryGetEnemyPosition(500, out _),
            "Broad NPC routing invented a position for a nonpositional native sound");
        fixture.Pool->IsPositional = 1;
        fixture.Pool->Position = new(float.NaN, 0, 0);
        check(!ownership.TryGetEnemyPosition(500, out _), "Broad NPC routing admitted a nonfinite source position");
        fixture.Pool->Position = new(3, 0, 4);
        for (var i = 0; i < 100; i++) ownership.EnemyKindOf(500);
        Action lookup = () =>
        {
            ownership.EnemyKindOf(500);
            ownership.TryGetEnemyPosition(500, out _);
        };
        var allocated = AllocationChecks.MeasureAllocations(lookup, warmup: 0, iterations: 10000);
        check(allocated == 0 && reads == 12,
            $"Broad NPC audio lookups allocated {allocated} bytes or repeated cached native filename reads");
    }

    private static void CheckOwnershipLifetime(Action<bool, string> check)
    {
        using var fixture = new Fixture();
        var ownership = fixture.Ownership;
        var npc = ownership.ResolveCaster(Npc.Actor, 0)!;
        fixture.Reserve(ownership.Player);
        check(ownership.Contains(500) && ownership.EnemyKindOf(500) == EnemySoundKind.None,
            "The local player's SFX entered the boss NPC channel");
        check(ownership.ResolveCaster(300, 30000) == null && ownership.ResolveTimelineCaster(30, 0) == null,
            "An unregistered other-player/pet identity was inferred from an actor or index");
        fixture.Reserve(null);
        check(!ownership.Contains(500) && ownership.EnemyKindOf(500) == EnemySoundKind.None,
            "Unknown VFX inherited a previous local/NPC owner");
        ownership.Acquired(fixture.Pool, null, 1, Npc.Actor, Npc.DrawObject, 20);
        ownership.LinkPlayback(-20, 500);
        check(ownership.EnemyKindOf(500) == EnemySoundKind.None,
            "Raw actor/draw/index metadata fabricated a resolved NPC ownership identity");
        fixture.Reserve(npc);
        fixture.Pool->SoundId = -21;
        check(ownership.EnemyKindOf(500) == EnemySoundKind.None,
            "A stale playback link matched a changed native reservation ID");
        fixture.Reserve(npc);
        ownership.Acquired(fixture.Pool, null);
        check(ownership.EnemyKindOf(500) == EnemySoundKind.None && ownership.EnemyKindOf(-20) == EnemySoundKind.None,
            "Pool-slot reuse retained the prior NPC owner or accepted a negative playback ID");
        fixture.Reserve(npc);
        fixture.Pool->IsActive = 0;
        check(ownership.EnemyKindOf(500) == EnemySoundKind.None, "A released NPC sound retained added vibration");

        fixture.Reserve(npc);
        ownership.SetEnemies(new[] { Npc with { EntityId = 2001 } }, 1);
        check(ownership.EnemyKindOf(500) == EnemySoundKind.None,
            "Reusing an NPC address/index for another entity retained old sound ownership");
        npc = ownership.ResolveCaster(Npc.Actor, 0)!;
        fixture.Reserve(npc);
        ownership.SetEnemies(new[] { Npc with { EntityId = 2001 } }, 2);
        check(ownership.EnemyKindOf(500) == EnemySoundKind.None, "A scene change retained an old NPC sound lease");
        npc = ownership.ResolveCaster(Npc.Actor, 0)!;
        fixture.Reserve(npc);
        ownership.SetEnemies(new[] { Npc with { EntityId = 2001, Role = EnemyActorRole.Combatant } }, 2);
        check(ownership.EnemyKindOf(500) == EnemySoundKind.None,
            "Ending the broad encounter policy retained its old owner identity");
        fixture.Reserve(ownership.ResolveCaster(Npc.Actor, 0));
        check(ownership.EnemyKindOf(500) == EnemySoundKind.None,
            "An untagged reservation retained broad eligibility after the role changed");
        ownership.SetEnemies(new[] { Npc }, 3);
        fixture.Reserve(ownership.ResolveCaster(Npc.Actor, 0));
        ownership.SetPlayer(101, 1001, 10001);
        check(ownership.EnemyKindOf(500) == EnemySoundKind.None,
            "A local player/session change retained an old NPC sound identity");
    }

    private static void CheckLegacyFilters(Action<bool, string> check)
    {
        var marker = false;
        using var fixture = new Fixture(_ => marker);
        var ownership = fixture.Ownership;
        var regular = Npc with { Kind = EnemySoundKind.Regular, Role = EnemyActorRole.Combatant };
        ownership.SetEnemies(new[] { regular }, 1);
        var npc = ownership.ResolveCaster(regular.Actor, 0);
        foreach (var tag in new[] { default(EnemyActionTag), new EnemyActionTag(EnemyActionKind.AutoAttack, 7) })
        {
            fixture.Reserve(npc, tag);
            check(ownership.EnemyKindOf(500) == EnemySoundKind.None,
                "The new boss policy changed regular-enemy unknown/basic-attack filtering");
        }
        fixture.Reserve(npc, new(EnemyActionKind.Skill, TimelineId: 130, Phase: EnemyActionPhase.Caster));
        check(ownership.EnemyKindOf(500) == EnemySoundKind.Regular,
            "The new boss policy changed the existing regular skill/cast behavior");
        ownership.SetEnemies(new[] { Npc with { Role = EnemyActorRole.Combatant } }, 1);
        npc = ownership.ResolveCaster(Npc.Actor, 0);
        fixture.Reserve(npc, new(EnemyActionKind.Skill, TimelineId: 130, Phase: EnemyActionPhase.Caster));
        check(ownership.EnemyKindOf(500) == EnemySoundKind.None, "A legacy boss's casting layer became eligible");
        marker = true;
        fixture.Reserve(npc, Released);
        check(ownership.EnemyKindOf(500) == EnemySoundKind.None,
            "Legacy released-skill routing bypassed its existing targeting-cue filter");
    }

    private static void CheckAudioScope(Action<bool, string> check)
    {
        var settings = new HapticSettings(BossEnabled: true, EnemyStrength: 0.6f);
        foreach (var bus in new byte[] { 2, 10 })
            check(settings.AdditionalGain(false, bus, 0, false, EnemySoundKind.Boss) == 0.6f,
                "The existing NPC SFX buses lost their configured haptic gain");
        foreach (var bus in new byte[] { 0, 1, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 })
            check(settings.AdditionalGain(false, bus, 0, false, EnemySoundKind.Boss) == 0,
                "NPC ownership alone admitted UI, music, dialogue, footsteps or unleased ambience");
        foreach (var flags in new uint[] { 2, 4, 8, 6, 10, 12, 14 })
            check(settings.AdditionalGain(false, 2, flags, false, EnemySoundKind.Boss) == 0,
                "Broad NPC SFX duplicated a native vibration send or vibration-only clip");
        check(settings.AdditionalGain(false, 2, 0, true, EnemySoundKind.Boss) == 0 &&
            new HapticSettings().AdditionalGain(false, 2, 0, false, EnemySoundKind.Boss) == 0,
            "Already-routed audio or a disabled boss channel gained added vibration");
    }
}
