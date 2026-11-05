// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using DualSenseLighting.Core;

internal static unsafe class EnemyCasterOriginChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckNativeLayout(check);
        CheckLayoutValidation(check);
        CheckAttackTypeEvidence(check);
        CheckAttackRouting(check);
    }

    private const int ActorTable = 1234;

    private static void Initialize(byte* clip, byte* actor, byte* entry)
    {
        new Span<byte>(clip, 0xD0).Clear(); new Span<byte>(actor, 0x60).Clear(); new Span<byte>(entry, 0x20).Clear();
        *(nint*)actor = ActorTable; *(uint*)(actor + 0x50) = 1;
        *(uint*)(clip + 0x84) = 0x34; *(nint*)(clip + 0x48) = (nint)(entry + 8);
        *(nint*)(clip + 0x28) = (nint)actor;
        *(uint*)entry = 0x33363043; *(int*)(entry + 4) = 0x20;
        *(int*)(entry + 8 + 4) = 1; entry[8 + 0x14] = 1;
    }

    private static void CheckNativeLayout(Action<bool, string> check)
    {
        var clip = stackalloc byte[0xD0]; var actor = stackalloc byte[0x60]; var entry = stackalloc byte[0x20];
        Initialize(clip, actor, entry);
        check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == EnemySoundBinding.CasterOrigin,
            "A validated positional duration-one at the caster origin was still classified as a body attachment");
        check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, false) == EnemySoundBinding.Caster,
            "Unvalidated native position support still promoted a caster-origin effect");
        check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable) == EnemySoundBinding.Caster,
            "A caller without explicit position-layout validation enabled the optional extension");
        // The bind byte is ignored when Use_Bind_Id is clear. It is the flag,
        // rather than a particular bone number, that establishes the native route.
        entry[8 + 0x15] = 71;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == EnemySoundBinding.CasterOrigin,
            "An unused bind byte overrode the native no-bone-binding flag");
        foreach (var flags in new byte[] { 0, 2, 3, 4, 5, 6, 7, 0x80, 0x81, 0xFF })
        {
            entry[8 + 0x14] = flags;
            check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == EnemySoundBinding.Caster,
                "A non-positional, stopped, bone-bound or unfamiliar caster track acquired origin-effect eligibility");
        }
        entry[8 + 0x14] = 1;
        foreach (var duration in new[] { 0, -1, 2, int.MinValue, int.MaxValue })
        {
            *(int*)(entry + 8 + 4) = duration;
            check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == EnemySoundBinding.Caster,
                "An unsupported-duration caster track acquired origin-effect eligibility");
        }
        *(int*)(entry + 8 + 4) = 1; entry[8 + 0x14] = 5;
        foreach (var (selector, expected) in new[] { (0u, EnemySoundBinding.Default), (2u, EnemySoundBinding.Target) })
        {
            *(uint*)(actor + 0x50) = selector;
            check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == expected,
                "Origin-effect support changed existing default/target tracks with bone bindings");
            check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, false) == expected,
                "Disabling unsupported origin-effect handling disabled proven default/target sound bindings");
        }
        *(uint*)(actor + 0x50) = 1; entry[8 + 0x14] = 1;
        var track = stackalloc byte[0x18]; new Span<byte>(track, 0x18).Clear();
        *(nint*)(clip + 0x28) = 0; *(nint*)(clip + 8) = (nint)track; *(nint*)(track + 8) = (nint)actor;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == EnemySoundBinding.CasterOrigin,
            "First-use origin effects depended on a populated native actor cache");
        *(nint*)(clip + 0x28) = (nint)actor;
        *(uint*)(actor + 0x50) = 3;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == EnemySoundBinding.Unknown,
            "An unsupported scheduler selector became a caster-origin effect");
        *(uint*)(actor + 0x50) = 1; *(nint*)actor = ActorTable + 1;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == EnemySoundBinding.Unknown,
            "An unfamiliar scheduler actor acquired origin-effect eligibility");
        *(nint*)actor = ActorTable; *(uint*)(clip + 0x84) = 0x29; *(uint*)entry = 0x33353043;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == EnemySoundBinding.Unknown,
            "A C053 character/voice clip was treated as a caster-origin attack");
        *(uint*)(clip + 0x84) = 0x34;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == EnemySoundBinding.Unknown,
            "A mismatched sound entry header acquired origin-effect eligibility");
        *(uint*)entry = 0x33363043; *(int*)(entry + 4) = 0x1C;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == EnemySoundBinding.Unknown,
            "An incompatible C063 body size was parsed using the origin-effect offsets");
        *(int*)(entry + 4) = 0x20; *(nint*)(clip + 0x48) = 0;
        check(GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true) == EnemySoundBinding.Unknown &&
            GameAudioLayout.ClipSoundBinding(0, ActorTable, true) == EnemySoundBinding.Unknown &&
            GameAudioLayout.ClipSoundBinding((nint)clip, 0, true) == EnemySoundBinding.Unknown,
            "Missing native clip/data/binding support invented an origin effect");

        Initialize(clip, actor, entry);
        var execution = new EnemyActionTag(EnemyActionKind.Skill, TimelineId: 3209, Phase: EnemyActionPhase.Execution, HasAttackType: true);
        for (var i = 0; i < 2000; i++)
            EnemySkillCatalog.TimelineSound(execution, GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true));
        var before = GC.GetAllocatedBytesForCurrentThread(); var accepted = 0;
        for (var i = 0; i < 10000; i++)
            if (EnemySkillCatalog.TimelineSound(execution, GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true)).IsReleasedEffect) accepted++;
        check(GC.GetAllocatedBytesForCurrentThread() == before && accepted == 10000,
            "Native caster-origin binding/tag lookup allocated or lost effect evidence in steady state");
    }

    private static void CheckAttackTypeEvidence(Action<bool, string> check)
    {
        var catalog = new EnemySkillCatalog(new[] {
            new EnemyActionDefinition(100, 3, 900, 0, 130, AttackType: 5),
            new EnemyActionDefinition(101, 3, 901, 960, 130), // Weapon/setup action: no typed attack.
            new EnemyActionDefinition(102, 3, 902, 0, 130, AttackType: uint.MaxValue),
            new EnemyActionDefinition(103, 3, 903, 0, 130, AttackType: 9),
            new EnemyActionDefinition(200, 3, 910, 0, 0, AttackType: 5),
            new EnemyActionDefinition(201, 3, 911, 0, 0), // Different row, same fixed key.
            new EnemyActionDefinition(202, 3, 912, 0, 0, AttackType: 1),
            new EnemyActionDefinition(203, 3, 913, 0, 0, AttackType: 8),
            new EnemyActionDefinition(300, 3, 920, 0, 0, AttackType: 5),
            new EnemyActionDefinition(301, 3, 920, 0, 0), // Same template, setup action on one actor.
        }, new[] {
            new EnemyTimelineDefinition(900, "fixture/typed"), new EnemyTimelineDefinition(901, "fixture/setup"),
            new EnemyTimelineDefinition(902, "fixture/sentinel"), new EnemyTimelineDefinition(903, "fixture/unsupported"),
            new EnemyTimelineDefinition(910, "fixture/fixed/mixed"), new EnemyTimelineDefinition(911, "fixture/fixed/mixed"),
            new EnemyTimelineDefinition(912, "fixture/fixed/typed"), new EnemyTimelineDefinition(913, "fixture/fixed/typed"),
            new EnemyTimelineDefinition(920, "mon_sp/[SKL_ID]/mon_sp009"),
            new EnemyTimelineDefinition(960, "fixture/hit"), new EnemyTimelineDefinition(130, "fixture/cast") });
        var typed = catalog.TimelineKey("fixture/typed"u8);
        check(typed.HasAttackType && catalog.Action(100, 1).HasAttackType &&
            EnemySkillCatalog.TimelineSound(typed, EnemySoundBinding.CasterOrigin).IsReleasedEffect,
            "Recognized native attack metadata did not qualify an execution-origin effect");
        check(!catalog.Action(100, 2).IsSkill &&
            !EnemySkillCatalog.TimelineSound(catalog.Action(100, 2), EnemySoundBinding.CasterOrigin).IsReleasedEffect,
            "Typed metadata admitted an unsupported native action type");
        foreach (var (key, action) in new[] { ("fixture/setup", 101u), ("fixture/sentinel", 102u), ("fixture/unsupported", 103u) })
        {
            var tag = catalog.TimelineKey(Encoding.ASCII.GetBytes(key));
            var origin = EnemySkillCatalog.TimelineSound(tag, EnemySoundBinding.CasterOrigin);
            check(tag.IsSkill && !tag.HasAttackType && !catalog.Action(action, 1).HasAttackType &&
                !origin.IsReleasedEffect && origin.Phase == EnemyActionPhase.Caster,
                "Untyped, sentinel or unknown attack metadata promoted a setup/weapon-change origin sound");
            foreach (var binding in new[] { EnemySoundBinding.Default, EnemySoundBinding.Target })
                check(EnemySkillCatalog.TimelineSound(tag, binding).IsReleasedEffect,
                    "The new caster-origin attack-type gate changed existing default/target execution tracks");
        }
        var fixedMixed = catalog.TimelineKey("fixture/fixed/mixed"u8);
        check(fixedMixed.IsSkill && !fixedMixed.HasAttackType &&
            !EnemySkillCatalog.TimelineSound(fixedMixed, EnemySoundBinding.CasterOrigin).IsReleasedEffect,
            "A fixed timeline key shared by a typed attack and setup row was assumed to be attack-only");
        var fixedTyped = catalog.TimelineKey("fixture/fixed/typed"u8);
        check(fixedTyped.HasAttackType && EnemySkillCatalog.TimelineSound(fixedTyped, EnemySoundBinding.CasterOrigin).IsReleasedEffect,
            "Different recognized attack types sharing a key were mistaken for typed/setup ambiguity");
        var typedActor = new GameAudioLayout.PlayerIdentity(10, 100, 1000); typedActor.ObserveAction(300);
        var mixedActor = new GameAudioLayout.PlayerIdentity(20, 200, 2000); mixedActor.ObserveAction(300); mixedActor.ObserveAction(301);
        var setupActor = new GameAudioLayout.PlayerIdentity(30, 300, 3000); setupActor.ObserveAction(301);
        var typedTemplate = catalog.TimelineKey("mon_sp/m0487/mon_sp009"u8, typedActor);
        check(typedTemplate.IsSkill && typedTemplate.HasAttackType &&
            EnemySkillCatalog.TimelineSound(typedTemplate, EnemySoundBinding.CasterOrigin).IsReleasedEffect,
            "Another actor's setup action poisoned a typed-only actor's model-resolved attack evidence");
        foreach (var actor in new[] { mixedActor, setupActor })
        {
            var tag = catalog.TimelineKey("mon_sp/m0487/mon_sp009"u8, actor);
            check(tag.IsSkill && !tag.HasAttackType &&
                !EnemySkillCatalog.TimelineSound(tag, EnemySoundBinding.CasterOrigin).IsReleasedEffect,
                "Actor-specific setup or typed/setup ambiguity inherited caster-origin attack eligibility");
        }
        var hit = catalog.TimelineKey("fixture/hit"u8);
        check(hit.IsReleasedEffect && !hit.HasAttackType &&
            EnemySkillCatalog.TimelineSound(hit, EnemySoundBinding.Caster).IsReleasedEffect &&
            EnemySkillCatalog.TimelineSound(hit, EnemySoundBinding.CasterOrigin).IsReleasedEffect,
            "An explicit hit-phase effect required attack-type evidence or lost an existing bone-bound hit");
        check(catalog.ClassifySound(new(200, 2000, 20000, 20, EnemySoundKind.Boss),
                new(200, 2000, 20000), 3, default, EnemySkillCatalog.TimelineSound(hit, EnemySoundBinding.Caster)).IsReleasedEffect,
            "An explicitly released bone-bound hit was rejected by the root boss filter");
        for (uint attackType = 1; attackType <= 8; attackType++)
        {
            var known = new EnemySkillCatalog(new[] { new EnemyActionDefinition(1, 3, 10, 0, 0, AttackType: attackType) },
                new[] { new EnemyTimelineDefinition(10, "fixture/known") });
            check(known.Action(1, 1).HasAttackType && known.TimelineKey("fixture/known"u8).HasAttackType &&
                EnemySkillCatalog.TimelineSound(known.TimelineKey("fixture/known"u8), EnemySoundBinding.CasterOrigin).IsReleasedEffect,
                "A recognized native attack type was excluded from the generic origin-effect rule");
        }
        for (var i = 0; i < 2000; i++) catalog.TimelineKey("mon_sp/m0487/mon_sp009"u8, typedActor);
        var before = GC.GetAllocatedBytesForCurrentThread(); var count = 0;
        for (var i = 0; i < 10000; i++)
            if (catalog.TimelineKey("mon_sp/m0487/mon_sp009"u8, typedActor).HasAttackType && catalog.Action(100, 1).HasAttackType) count++;
        check(count == 10000 && GC.GetAllocatedBytesForCurrentThread() == before,
            "Native attack-type evidence allocated during actor/template or direct action lookup");
    }

    private static void CheckLayoutValidation(Action<bool, string> check)
    {
        var body = new byte[0x110];
        new byte[] { 0x48, 0x8B, 0x69, 0x48 }.CopyTo(body, 0x43);
        new byte[] { 0x83, 0xF8, 0x34 }.CopyTo(body, 0x3A);
        new byte[] { 0xF6, 0x45, 0x14, 0x01 }.CopyTo(body, 0x47);
        new byte[] { 0xF6, 0x45, 0x14, 0x04 }.CopyTo(body, 0x99);
        new byte[] { 0x0F, 0xB6, 0x55, 0x15 }.CopyTo(body, 0xBB);
        check(GameAudioLayout.ClipPositionLayout(body) && !GameAudioLayout.ClipPositionLayout(body.AsSpan(0, 0x10F)),
            "Native sound-position validation rejected the pinned fields or accepted truncated code");
        foreach (var offset in new[] { 0x3C, 0x46, 0x49, 0x9B, 0xBE })
        {
            var before = body[offset]; body[offset]++;
            check(!GameAudioLayout.ClipPositionLayout(body),
                "Changed C063 type, positional/bone flag offset or bind-byte offset passed native validation");
            body[offset] = before;
        }
    }

    private static void CheckAttackRouting(Action<bool, string> check)
    {
        var clip = stackalloc byte[0xD0]; var nativeActor = stackalloc byte[0x60]; var entry = stackalloc byte[0x20];
        Initialize(clip, nativeActor, entry);
        var pool = (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed((nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        try
        {
            var ownership = new GameAudioLayout.PlayerSoundOwnership(pool); ownership.SetPlayer(100, 1000, 10000);
            var settings = new HapticSettings(BossEnabled: true, EnemyStrength: 0.6f);
            // First the captured GunAtk and UltRaid timelines, then unrelated
            // metadata/actor identities. These fixtures do not add production IDs.
            for (var i = 0; i < 16; i++)
            {
                uint timeline = i == 0 ? 3209u : i == 1 ? 4573u : (uint)(24000 + i);
                uint action = i == 0 ? 11198u : i == 1 ? 11207u : (uint)(60000 + i);
                var key = i == 0 ? "mon_sp/m0487/mon_sp009" : i == 1 ? "mon_sp/m0487/hide/mon_sp001" : "fixture/origin/attack";
                var template = i < 2 ? key.Replace("m0487", "[SKL_ID]", StringComparison.Ordinal) : key;
                var catalog = new EnemySkillCatalog(new[] { new EnemyActionDefinition(action, 3, timeline, 1875, 130, AttackType: i < 2 ? 5u : (uint)(1 + i % 8)),
                    new EnemyActionDefinition(7, 1, uint.MaxValue, 1875, 0) }, new[] {
                    new EnemyTimelineDefinition(timeline, template), new EnemyTimelineDefinition(1875, "fixture/shared"),
                    new EnemyTimelineDefinition(130, "fixture/cast") });
                var boss = new GameAudioLayout.EnemyActor(200, (uint)(2000 + i), 20000, 20, EnemySoundKind.Boss,
                    NameId: i < 2 ? 7225u : (uint)(90000 + i), BaseId: i < 2 ? 8720u : (uint)(91000 + i));
                ownership.SetEnemies(new[] { boss }, (uint)(778 + i));
                var identity = ownership.ResolveCaster(boss.Actor, 0)!; identity.ObserveAction(action);
                var execution = catalog.TimelineKey(Encoding.ASCII.GetBytes(key), identity);
                var binding = GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true);
                var track = EnemySkillCatalog.TimelineSound(execution, binding);
                var release = catalog.ClassifySound(boss, identity, 3, default, track);
                check(execution.Phase == EnemyActionPhase.Execution && execution.HasAttackType && binding == EnemySoundBinding.CasterOrigin &&
                    release.IsReleasedEffect && release.TimelineId == timeline && release.Binding == EnemySoundBinding.CasterOrigin,
                    "A caster-origin line-stack/raid-wide pattern lost confirmed execution evidence");
                foreach (var rejected in new[] { catalog.TimelineKey("fixture/cast"u8), catalog.TimelineKey("fixture/shared"u8),
                    catalog.Action(7, 1), default(EnemyActionTag),
                    new EnemyActionTag(EnemyActionKind.Skill, TimelineId: timeline, Phase: EnemyActionPhase.Unknown, HasAttackType: true),
                    new EnemyActionTag(EnemyActionKind.Skill, TimelineId: timeline, Phase: EnemyActionPhase.Caster, HasAttackType: true),
                    new EnemyActionTag(EnemyActionKind.AutoAttack, 7, HasAttackType: true) })
                    check(!EnemySkillCatalog.TimelineSound(rejected, binding).IsReleasedEffect,
                        "Caster-origin support promoted casting, shared/unknown or basic-attack timelines");
                check(!catalog.ClassifySound(boss, identity, 2, default, track).IsReleasedEffect &&
                    !catalog.ClassifySound(boss, identity, 3, catalog.Action(7, 1), track).IsReleasedEffect,
                    "Caster-origin support admitted character/voice sound or overrode an explicit auto-attack");
                pool->IsActive = 1; pool->SoundId = 123; pool->IsPositional = 1; pool->Position = new Vector3(4, 0, 3);
                ownership.Acquired(pool, identity, 3, action: release);
                check(ownership.EnemyKindOf(123) == EnemySoundKind.Boss && !ownership.Contains(123) &&
                    ownership.TryGetEnemyPosition(123, out var position) && position == new Vector3(4, 0, 3) &&
                    settings.AdditionalGain(false, 2, 0, false, ownership.EnemyKindOf(123)) == 0.6f,
                    "A confirmed origin effect failed the boss-only route or contaminated player sound ownership");
                // Captured 拉格 body/preparation uses flags 5 and bind point 71.
                // Preserve that exclusion while enabling the unbound impacts.
                entry[8 + 0x14] = 5; entry[8 + 0x15] = 71;
                var body = EnemySkillCatalog.TimelineSound(execution, GameAudioLayout.ClipSoundBinding((nint)clip, ActorTable, true));
                ownership.Acquired(pool, identity, 3, action: catalog.ClassifySound(boss, identity, 3, default, body));
                check(body.Binding == EnemySoundBinding.Caster && !body.IsReleasedEffect && ownership.EnemyKindOf(123) == EnemySoundKind.None,
                    "Enabling caster-origin impacts restored the previously excluded bone-bound body track");
                entry[8 + 0x14] = 1; entry[8 + 0x15] = 0;
            }
        }
        finally { NativeMemory.Free(pool); }
    }
}
