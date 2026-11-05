// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using DualSenseLighting.Core;

internal static class EnemySkillChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckHookedCallback(check);
        EnemyUncategorizedSkillChecks.Run(check);
        var catalog = new EnemySkillCatalog(new[]
        {
            new EnemyActionDefinition(7, 1, uint.MaxValue, 1875, 0), // Basic melee attack.
            new EnemyActionDefinition(8, 1, uint.MaxValue - 1, 1876, 0),
            new EnemyActionDefinition(870, 1, uint.MaxValue, 1875, 0),
            new EnemyActionDefinition(39021, 3, 3203, 1875, 8), // 强击 shares a normal hit timeline.
            new EnemyActionDefinition(2183, 3, 3063, 3064, 8), // Another 强击 definition.
            new EnemyActionDefinition(500, 1, 3203, 1875, 0), // Model-dependent end used by an auto-attack too.
            new EnemyActionDefinition(501, 1, 5000, 0, 0),
            new EnemyActionDefinition(600, 10, 6000, 0, 0), // System actions are not skills.
        }, new[]
        {
            new EnemyTimelineDefinition(1875, "normal_hit/normal_hit"),
            new EnemyTimelineDefinition(1876, "normal_hit/fast_attack_hit"),
            new EnemyTimelineDefinition(3203, "mon_sp/[SKL_ID]/mon_sp003"),
            new EnemyTimelineDefinition(3063, "mon_sp/m0176/mon_sp001"),
            new EnemyTimelineDefinition(3064, "mon_sp/m0176/mon_sp001_hit"),
            new EnemyTimelineDefinition(8, "cast/monster"),
            new EnemyTimelineDefinition(5000, "auto/ranged"),
            new EnemyTimelineDefinition(6000, "system/other"),
            new EnemyTimelineDefinition(68, "battle/damage"),
            new EnemyTimelineDefinition(69, "battle/damage_continuous"),
            new EnemyTimelineDefinition(62, "normal/damage"),
            new EnemyTimelineDefinition(63, "normal/damage_continuous"),
            new EnemyTimelineDefinition(78, "battle/weapon_guard"),
            new EnemyTimelineDefinition(79, "battle/shield_guard"),
        });
        check(catalog.Action(7, 1).Kind == EnemyActionKind.AutoAttack && !catalog.Action(7, 1).IsSkill &&
            catalog.Action(8, 1).Kind == EnemyActionKind.AutoAttack, "Native auto-attack actions became skills");
        check(catalog.Action(39021, 1).IsSkill && catalog.Action(39021, 1).ActionId == 39021 && catalog.Action(2183, 1).IsSkill,
            "强击 weaponskills were classified as basic attacks");
        check(!catalog.Action(39021, 2).IsSkill && !catalog.Action(999999, 1).IsSkill && !catalog.Action(600, 1).IsSkill,
            "Unknown action types/IDs or system actions were accepted as skills");
        check(!catalog.TimelineKey("normal_hit/normal_hit"u8).IsSkill &&
            catalog.TimelineKey("normal_hit/normal_hit"u8).Kind == EnemyActionKind.Unknown,
            "A shared hit timeline admitted basic attacks or was assumed to belong only to them");
        check(catalog.TimelineKey("normal_hit/fast_attack_hit"u8).Kind == EnemyActionKind.AutoAttack &&
            catalog.TimelineKey("auto/ranged"u8).Kind == EnemyActionKind.AutoAttack,
            "Basic attack timelines were not excluded");
        check(!catalog.TimelineKey("mon_sp/[SKL_ID]/mon_sp003"u8).IsSkill,
            "A shared model-dependent animation was guessed to be a skill");
        var skill = catalog.TimelineKey("mon_sp/m0176/mon_sp001"u8);
        check(skill.IsSkill && skill.TimelineId == 3063 &&
            catalog.TimelineKey("mon_sp/m0176/mon_sp001_hit"u8).IsSkill && catalog.TimelineKey("cast/monster"u8).IsSkill,
            "Unambiguous skill cast/animation/hit timelines were lost");
        check(skill.Phase == EnemyActionPhase.Execution && !skill.IsReleasedEffect &&
            !catalog.TimelineKey("cast/monster"u8).IsReleasedEffect &&
            catalog.TimelineKey("mon_sp/m0176/mon_sp001_hit"u8).IsReleasedEffect,
            "Boss caster tracks were not separated from released hit effects");
        check(catalog.Action(39021, 1).IsReleasedEffect,
            "A confirmed native skill result lost its release evidence");
        check(!catalog.TimelineKey("unlisted/path"u8).IsSkill && !catalog.TimelineKey("system/other"u8).IsSkill &&
            !catalog.TimelineKey(ReadOnlySpan<byte>.Empty).IsSkill && !catalog.TimelineKey(new byte[256]).IsSkill &&
            !catalog.TimelineKey(new byte[] { 0xff, 0x80 }).IsSkill,
            "Unknown, oversized, empty or non-ASCII keys were treated as confirmed skills");
        check(!new EnemyActionTag(EnemyActionKind.Skill).IsSkill,
            "An enemy sound with neither action nor timeline evidence became a skill");
        check(catalog.PlayerTimelineKey("battle/damage"u8).IsHitReaction &&
            catalog.PlayerTimelineKey("battle/damage"u8).TimelineId == 68 &&
            catalog.PlayerTimelineKey("battle/damage_continuous"u8).IsHitReaction,
            "Delayed player hurt sounds depended on an expired synchronous action scope");
        // Captured v0.14.5 player keys were normal/damage_continuous and battle/weapon_guard.
        foreach (var (key, id) in new[] { ("normal/damage", 62u), ("normal/damage_continuous", 63u),
            ("battle/weapon_guard", 78u), ("battle/shield_guard", 79u) })
        {
            var tag = catalog.PlayerTimelineKey(System.Text.Encoding.ASCII.GetBytes(key));
            check(tag.IsHitReaction && tag.TimelineId == id && !tag.IsSkill,
                "Captured hurt/guard sound was not assigned exclusively to the optional reaction channel: " + key);
        }
        check(!catalog.PlayerTimelineKey("normal_hit/normal_hit"u8).IsHitReaction &&
            !catalog.PlayerTimelineKey("mon_sp/m0176/mon_sp001"u8).IsHitReaction &&
            !catalog.PlayerTimelineKey("normal/idle"u8).IsHitReaction,
            "The hit-reaction option accepted outgoing skill effects or unrelated player motions");
        var actor = new GameAudioLayout.PlayerIdentity(10, 100, 1000);
        actor.ObserveAction(870); actor.ObserveAction(39021);
        check(catalog.TimelineKey("mon_sp/m0350/mon_sp003"u8, actor).IsSkill &&
            catalog.TimelineKey("mon_sp/m0350/mon_sp003"u8, actor).TimelineId == 3203,
            "A confirmed actor's model-resolved skill timeline failed to match its game-data template");
        check(!catalog.TimelineKey("normal_hit/normal_hit"u8, actor).IsSkill,
            "Learning the actor's skill allowed its shared auto-attack hit timeline");
        var other = new GameAudioLayout.PlayerIdentity(20, 200, 2000); other.ObserveAction(870);
        check(!catalog.TimelineKey("mon_sp/m0350/mon_sp003"u8, other).IsSkill &&
            !catalog.TimelineKey("mon_sp/../mon_sp003"u8, actor).IsSkill,
            "Actor-specific skill evidence leaked to another actor or accepted an invalid skeleton segment");
        actor.ObserveAction(500);
        check(!catalog.TimelineKey("mon_sp/m0350/mon_sp003"u8, actor).IsSkill,
            "A model-resolved timeline shared by this actor's own basic attack and skill was guessed to be a skill");
        CheckBossSounds(check, catalog);
        EnemyClipChecks.Run(check, catalog);
        EnemyCasterOriginChecks.Run(check);
        NpcSpawnMetadataChecks.Run(check);
        CheckHitTargets(check);
        for (var i = 0; i < 2000; i++) catalog.TimelineKey("mon_sp/m0176/mon_sp001_hit"u8);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        for (var i = 0; i < 10000; i++)
            if (catalog.TimelineKey("mon_sp/m0176/mon_sp001_hit"u8).IsSkill && catalog.Action(39021, 1).IsSkill) count++;
        check(GC.GetAllocatedBytesForCurrentThread() == before && count == 10000,
            "Skill metadata lookup allocated on the native sound path");
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        { actor.ObserveAction(39021); catalog.TimelineKey("mon_sp/m0350/mon_sp003"u8, actor); }
        check(GC.GetAllocatedBytesForCurrentThread() == before, "Per-actor skill history or template lookup allocated in native callbacks");
    }

    private static void CheckBossSounds(Action<bool, string> check, EnemySkillCatalog catalog)
    {
        var boss = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss);
        var helper = boss with { Actor = 210, Role = EnemyActorRole.BossMechanic };
        var actor = new GameAudioLayout.PlayerIdentity(200, 2000, 20000);
        var emitter = new GameAudioLayout.PlayerIdentity(210, 2100, 21000);
        var end = catalog.TimelineKey("mon_sp/m0176/mon_sp001"u8);
        var hit = catalog.TimelineKey("mon_sp/m0176/mon_sp001_hit"u8);
        var release = catalog.Action(2183, 1);
        var basic = catalog.Action(870, 1);
        check(!catalog.ClassifySound(boss, actor, 3, release, end).IsReleasedEffect &&
            !catalog.ClassifySound(boss, actor, 2, release, hit).IsReleasedEffect,
            "Boss animation/body audio leaked into released-effect feedback");
        check(catalog.ClassifySound(boss, actor, 3, default, hit).IsReleasedEffect &&
            catalog.ClassifySound(boss, actor, 1, release, default).IsReleasedEffect,
            "Known boss hit tracks or synchronously released AVFX were rejected");
        check(!catalog.ClassifySound(boss, actor, 1, default, default).IsReleasedEffect &&
            !catalog.ClassifySound(boss, actor, 0, release, default).IsReleasedEffect,
            "Unclassified boss body audio was promoted without release evidence");
        var mechanic = catalog.ClassifySound(helper, emitter, 1, default, default);
        check(mechanic.IsReleasedEffect && mechanic.Phase == EnemyActionPhase.BossMechanic &&
            mechanic.ActionId == 0 && mechanic.TimelineId == 0,
            "A registered mechanic emitter's AVFX was lost or given a fabricated skill ID");
        check(catalog.ClassifySound(helper, emitter, 3, default, end).IsReleasedEffect &&
            !catalog.ClassifySound(helper, emitter, 3, default, default).IsReleasedEffect &&
            !catalog.ClassifySound(helper, emitter, 2, release, hit).IsReleasedEffect,
            "Boss helpers accepted arbitrary body/unclassified clip sounds or lost confirmed mechanic tracks");
        foreach (var source in new[] { boss, helper })
            check(!catalog.ClassifySound(source, emitter, 1, basic, hit).IsReleasedEffect &&
                !catalog.ClassifySound(source, emitter, 1, default, basic).IsReleasedEffect,
                "Known auto-attacks became released boss mechanics");
        emitter.ObserveAction(870);
        check(!catalog.ClassifySound(helper, emitter, 1, default, default).IsReleasedEffect &&
            catalog.ClassifySound(helper, emitter, 1, release, default).IsReleasedEffect,
            "A basic-attacking helper's unknown AVFX was promoted, or a confirmed skill was lost");
        var regular = boss with { Kind = EnemySoundKind.Regular };
        check(catalog.ClassifySound(regular, actor, 3, default, end).IsSkill &&
            catalog.ClassifySound(regular, actor, 3, basic, end).Kind == EnemyActionKind.AutoAttack,
            "Boss filtering changed the verified regular-enemy skill behavior");
        var ambiguous = new EnemySkillCatalog(new[] { new EnemyActionDefinition(1, 3, 10, 11, 0),
            new EnemyActionDefinition(2, 3, 11, 0, 0) },
            new[] { new EnemyTimelineDefinition(10, "caster"), new EnemyTimelineDefinition(11, "shared") });
        actor.ObserveAction(1); actor.ObserveAction(2);
        check(ambiguous.TimelineKey("shared"u8).IsSkill && !ambiguous.TimelineKey("shared"u8).IsReleasedEffect &&
            !ambiguous.TimelineKey("shared"u8, actor).IsReleasedEffect,
            "A timeline used for both caster and impact tracks was assumed to be released-only");
        for (var i = 0; i < 2000; i++) catalog.ClassifySound(helper, emitter, 1, default, default);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) catalog.ClassifySound(helper, emitter, 1, default, default);
        check(GC.GetAllocatedBytesForCurrentThread() == before, "Boss sound classification allocated in audio callbacks");
    }

    private static void CheckHitTargets(Action<bool, string> check)
    {
        ulong[] targets = [111, 222]; var effects = new byte[128]; effects[0] = 3;
        check(!GameAudioLayout.PlayerSoundOwnership.HitsPlayer(222, targets, effects), "Another target's damage became a player hit reaction");
        foreach (var type in new byte[] { 3, 5, 6 })
        {
            effects[64] = type;
            check(GameAudioLayout.PlayerSoundOwnership.HitsPlayer(222, targets, effects), "Player damage/block/parry was not recognized");
        }
        foreach (var type in new byte[] { 0, 1, 4, 14 })
        {
            effects[64] = type;
            check(!GameAudioLayout.PlayerSoundOwnership.HitsPlayer(222, targets, effects), "Misses, heals or statuses became player hit reactions");
        }
        check(!GameAudioLayout.PlayerSoundOwnership.HitsPlayer(0, targets, effects) &&
            !GameAudioLayout.PlayerSoundOwnership.HitsPlayer(222, targets, effects.AsSpan(0, 127)),
            "Hit attribution accepted an invalid player or truncated target effects");
    }

    private static unsafe void CheckHookedCallback(Action<bool, string> check)
    {
        const int size = 512, offset = 48, length = 0x90;
        var live = stackalloc byte[size]; var original = stackalloc byte[size];
        new Span<byte>(original, size).Clear();
        byte[] prologue = [0x40, 0x55, 0x53, 0x56, 0x57, 0x41, 0x54];
        byte[] headerRead = [0x41, 0x0f, 0xb7, 0x49, 0x1c];
        prologue.CopyTo(new Span<byte>(original + offset, prologue.Length));
        headerRead.CopyTo(new Span<byte>(original + offset + 68, headerRead.Length));
        new ReadOnlySpan<byte>(original, size).CopyTo(new Span<byte>(live, size));
        // The observed live callback uses this seven-byte jump after an existing hook.
        new byte[] { 0xff, 0x24, 0x25, 0xd0, 0x11, 0xf0, 0x3f }.CopyTo(new Span<byte>(live + offset, 7));
        check(!GameAudioLayout.ActionEffectLayout(new ReadOnlySpan<byte>(live + offset, length)),
            "Regression fixture did not reproduce the patched action callback");
        var inspected = GameAudioLayout.InspectionAddress((nint)live + offset, (nint)live, (nint)original, size, length);
        check(inspected == (nint)original + offset && GameAudioLayout.ActionEffectLayout(new ReadOnlySpan<byte>((void*)inspected, length)),
            "An existing action hook disabled skill detection instead of validating original code");
        original[offset + 68] = 0;
        check(!GameAudioLayout.ActionEffectLayout(new ReadOnlySpan<byte>((void*)inspected, length)),
            "Using the original module copy bypassed incompatible action-layout validation");
        check(GameAudioLayout.InspectionAddress((nint)live - 1, (nint)live, (nint)original, size, length) == 0 &&
            GameAudioLayout.InspectionAddress((nint)live + size - length + 1, (nint)live, (nint)original, size, length) == 0 &&
            GameAudioLayout.InspectionAddress((nint)live, (nint)live, (nint)original, size, 0) == 0 &&
            GameAudioLayout.InspectionAddress((nint)live, (nint)live, 0, size, length) == 0,
            "Original-code inspection accepted a truncated, missing or out-of-range image");
    }
}
