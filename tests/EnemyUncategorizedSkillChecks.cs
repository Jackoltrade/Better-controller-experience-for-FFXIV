// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Text;
using DualSenseLighting.Core;

internal static class EnemyUncategorizedSkillChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckMetadataRule(check);
        CheckCapturedActions(check);
        CheckCastIsolation(check);
        CheckAmbiguity(check);
        CheckFirstUseTemplates(check);
        CheckFirstUseEffectOwnership(check);
        CheckAllocation(check);
    }

    private static EnemySkillCatalog Single(EnemyActionDefinition action) => new(new[] { action },
        new[] { new EnemyTimelineDefinition(action.End is > 0 and <= ushort.MaxValue ? action.End : action.Hit,
            "fixture/uncategorized"), new EnemyTimelineDefinition(130, "fixture/cast") });

    private static void CheckMetadataRule(Action<bool, string> check)
    {
        for (uint attack = 1; attack <= 8; attack++)
        {
            var id = 12000 + attack * 17; var timeline = 1000 + attack * 13;
            var catalog = Single(new(id, 0, timeline, 0, 130, attack));
            var direct = catalog.Action(id, 1); var execution = catalog.TimelineKey("fixture/uncategorized"u8);
            check(direct.IsSkill && direct.HasAttackType && direct.ActionId == id && execution.IsSkill &&
                execution.HasAttackType && execution.Phase == EnemyActionPhase.Execution &&
                EnemySkillCatalog.TimelineSound(execution, EnemySoundBinding.CasterOrigin).IsReleasedEffect,
                "A valid typed Category 0 execution required a particular action, timeline, or attack-type allowlist");
        }
        foreach (var (end, hit) in new[] { (1u, 0u), (65535u, 0u), (0u, 1u), (0u, 65535u),
            (uint.MaxValue, 200u), (200u, uint.MaxValue - 1) })
            check(Single(new(100, 0, end, hit, 130, 5)).Action(100, 1).IsSkill,
                "A Category 0 attack lost a valid end or hit timeline at the supported boundaries");
        foreach (var attack in new uint[] { 0, 9, uint.MaxValue })
        {
            var catalog = Single(new(100, 0, 1000, 0, 130, attack));
            check(catalog.Action(100, 1).Kind == EnemyActionKind.Unknown &&
                !catalog.TimelineKey("fixture/uncategorized"u8).IsSkill && !catalog.TimelineKey("fixture/cast"u8).IsSkill,
                "Untyped, sentinel, or unsupported Category 0 metadata promoted setup/casting audio");
        }
        foreach (var (end, hit) in new[] { (0u, 0u), (0u, uint.MaxValue), (uint.MaxValue, 0u),
            (65536u, uint.MaxValue), (uint.MaxValue, uint.MaxValue - 1) })
        {
            var catalog = new EnemySkillCatalog(new[] { new EnemyActionDefinition(100, 0, end, hit, 130, 5) },
                new[] { new EnemyTimelineDefinition(130, "fixture/cast") });
            check(catalog.Action(100, 1).Kind == EnemyActionKind.Unknown && !catalog.TimelineKey("fixture/cast"u8).IsSkill,
                "A cast-only or sentinel timeline was enough to invent a Category 0 attack");
        }
        foreach (var category in new uint[] { 5, 6, 7, 8, 10, 11, 12, 14, 16, 18, uint.MaxValue })
            check(Single(new(100, category, 1000, 0, 130, 5)).Action(100, 1).Kind == EnemyActionKind.Unknown,
                "The Category 0 exception broadened an unrelated action category");
        var auto = Single(new(7, 1, 1000, 0, 0, 5));
        check(auto.Action(7, 1).Kind == EnemyActionKind.AutoAttack && !auto.Action(7, 1).IsSkill,
            "A confirmed auto-attack became a skill merely because it had a typed attack and real timeline");
    }

    private static EnemySkillCatalog CapturedCatalog() => new(new[] {
        new EnemyActionDefinition(8519, 0, 3201, 3241, 130, 5),
        new EnemyActionDefinition(8542, 0, 3209, 1875, 130, 5),
        new EnemyActionDefinition(8668, 0, 5377, 5378, 0, 5),
        new EnemyActionDefinition(7, 1, uint.MaxValue, 1875, 0),
        new EnemyActionDefinition(100, 0, 6000, 0, 130),
    }, new[] {
        new EnemyTimelineDefinition(3201, "mon_sp/[SKL_ID]/mon_sp001"),
        new EnemyTimelineDefinition(3241, "mon_sp/[SKL_ID]/mon_sp001_hit"),
        new EnemyTimelineDefinition(3209, "mon_sp/[SKL_ID]/mon_sp009"),
        new EnemyTimelineDefinition(1875, "normal_hit/normal_hit"),
        new EnemyTimelineDefinition(5377, "mon_sp/gimmick/g3fa_boss_gimmick03"),
        new EnemyTimelineDefinition(5378, "mon_sp/gimmick/g3fa_boss_gimmick03_hit"),
        new EnemyTimelineDefinition(130, "battle/mon_sp_a_start"),
        new EnemyTimelineDefinition(6000, "fixture/setup"),
    });

    private static void CheckCapturedActions(Action<bool, string> check)
    {
        var catalog = CapturedCatalog();
        // Installed Lakshmi action rows. Model tokens below are synthetic, so
        // first-cast learning is tested independently of a particular model ID.
        foreach (var (action, timeline, key) in new[] { (8519u, 3201u, "mon_sp/m0555/mon_sp001"),
            (8542u, 3209u, "mon_sp/m0555/mon_sp009"), (8668u, 5377u, "mon_sp/gimmick/g3fa_boss_gimmick03") })
        {
            var actor = new GameAudioLayout.PlayerIdentity(200, 2000, 20000);
            var bytes = Encoding.ASCII.GetBytes(key);
            if (action != 8668)
                check(catalog.TimelineKey(bytes).IsSkill && catalog.TimelineKey(bytes).TimelineId == timeline,
                    "Unambiguous Category 0 model-template metadata was unavailable before the first action result");
            check(catalog.Action(action, 1).IsSkill && catalog.ObserveCast(actor, 2000, action, 1),
                "A captured Category 0/AttackType 5 action could not be learned at cast start");
            var learned = catalog.TimelineKey(bytes, actor);
            var released = EnemySkillCatalog.TimelineSound(learned, EnemySoundBinding.CasterOrigin);
            var boss = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss);
            check(learned.IsSkill && learned.TimelineId == timeline && learned.Phase == EnemyActionPhase.Execution &&
                learned.HasAttackType && catalog.ClassifySound(boss, actor, 3, default, released).IsReleasedEffect,
                "First-cast Category 0 evidence did not reach the existing released boss-effect route");
            check(!catalog.ClassifySound(boss, actor, 2, default, released).IsReleasedEffect &&
                !catalog.ClassifySound(boss, actor, 3, catalog.Action(7, 1), released).IsReleasedEffect,
                "Recognizing an uncategorized skill weakened the body-sound or explicit auto-attack exclusions");
        }
        check(!catalog.TimelineKey("normal_hit/normal_hit"u8).IsSkill &&
            !EnemySkillCatalog.TimelineSound(catalog.TimelineKey("battle/mon_sp_a_start"u8), EnemySoundBinding.CasterOrigin).IsReleasedEffect,
            "Captured Category 0 actions made a shared basic-hit or cast-start timeline released-only");
    }

    private static bool EmptyHistory(GameAudioLayout.PlayerIdentity actor)
    {
        for (var i = 0; i < GameAudioLayout.PlayerIdentity.ActionCapacity; i++)
            if (actor.ObservedAction(i) != 0) return false;
        return true;
    }

    private static void CheckCastIsolation(Action<bool, string> check)
    {
        var catalog = CapturedCatalog();
        foreach (var actor in new[] { new GameAudioLayout.PlayerIdentity(0, 2000, 20000),
            new GameAudioLayout.PlayerIdentity(200, 0, 20000), new GameAudioLayout.PlayerIdentity(200, 0xE0000000, 20000) })
            check(!catalog.ObserveCast(actor, actor.EntityId, 8519, 1) && EmptyHistory(actor),
                "A missing actor or invalid native entity ID acquired cast history");
        foreach (var (entity, action, type) in new[] { (2001u, 8519u, (byte)1), (2000u, 8519u, (byte)0),
            (2000u, 8519u, (byte)2), (2000u, 8519u, (byte)255), (2000u, 7u, (byte)1),
            (2000u, 100u, (byte)1), (2000u, 0u, (byte)1), (2000u, 999999u, (byte)1),
            (2000u, uint.MaxValue, (byte)1) })
        {
            var actor = new GameAudioLayout.PlayerIdentity(200, 2000, 20000);
            check(!catalog.ObserveCast(actor, entity, action, type) && EmptyHistory(actor),
                "An unrelated entity, action type, unknown/setup action, or basic attack polluted cast history");
        }
        var known = new GameAudioLayout.PlayerIdentity(200, 2000, 20000);
        var other = new GameAudioLayout.PlayerIdentity(300, 3000, 30000);
        var replacement = new GameAudioLayout.PlayerIdentity(200, 2001, 20000);
        check(catalog.ObserveCast(known, 2000, 8519, 1) &&
            EmptyHistory(other) &&
            !catalog.ObserveCast(replacement, 2000, 8519, 1) && EmptyHistory(replacement),
            "First-cast evidence leaked to another actor or to a reused actor address with a different entity ID");
        check(known.ObservedAction(0) == 8519 && known.ObservedAction(1) == 0 &&
            !catalog.TimelineKey("mon_sp/../mon_sp001"u8, known).IsSkill &&
            !catalog.TimelineKey("mon_sp/a/b/mon_sp001"u8, known).IsSkill,
            "One observed cast changed unrelated action history or admitted an invalid model-template segment");
        check(catalog.ObserveCast(other, 3000, 8542, 1) &&
            other.ObservedAction(0) == 8542 &&
            catalog.TimelineKey("mon_sp/unrelated_model/mon_sp009"u8, other).IsSkill,
            "The generic cast observer required a boss/model allowlist for a valid different actor");
    }

    private static void CheckFirstUseTemplates(Action<bool, string> check)
    {
        var show = new EnemySkillCatalog(new[] { new EnemyActionDefinition(12000, 3, 4561, 0, 0, 5) },
            new[] { new EnemyTimelineDefinition(4561, "mon_sp/[SKL_ID]/show001") });
        var first = show.TimelineKey("mon_sp/m0500/show001"u8);
        check(first.IsSkill && first.TimelineId == 4561 && first.Phase == EnemyActionPhase.Execution &&
            EnemySkillCatalog.TimelineEffect(first).IsReleasedEffect,
            "A Byakko-style unambiguous presentation template required a previous cast or native template expansion");
        foreach (var bad in new[] { "mon_sp//show001", "mon_sp/../show001", "mon_sp/a/b/show001",
            "mon_sp/abcdefghijklmnopq/show001", "mon_sp/m0500/show002", "mon_sp/模型/show001" })
            check(!show.TimelineKey(Encoding.UTF8.GetBytes(bad)).IsSkill,
                "First-use template fallback accepted a missing/invalid model token, non-ASCII key, or different timeline");

        // Both templates match m1234. Ordering must not select whichever one
        // happens to sort first in the catalog.
        foreach (var broadSkill in new[] { false, true })
        {
            var overlap = new EnemySkillCatalog(new[] {
                new EnemyActionDefinition(900, broadSkill ? 0u : 1u, 700, 0, 0, 5),
                new EnemyActionDefinition(901, broadSkill ? 1u : 0u, 701, 0, 0, 5) }, new[] {
                new EnemyTimelineDefinition(700, "mon_sp/[SKL_ID]/attack"),
                new EnemyTimelineDefinition(701, "mon_sp/m[SKL_ID]/attack") });
            var tag = overlap.TimelineKey("mon_sp/m1234/attack"u8);
            check(tag.Kind == EnemyActionKind.Unknown && !EnemySkillCatalog.TimelineEffect(tag).IsReleasedEffect,
                "Overlapping first-use auto-attack/skill templates were resolved by first-match ordering");
        }
        var mixedPhases = new EnemySkillCatalog(new[] {
            new EnemyActionDefinition(900, 0, 700, 0, 0, 5), new EnemyActionDefinition(901, 0, 0, 701, 0, 5) }, new[] {
            new EnemyTimelineDefinition(700, "mon_sp/[SKL_ID]/attack"),
            new EnemyTimelineDefinition(701, "mon_sp/m[SKL_ID]/attack") });
        var phases = mixedPhases.TimelineKey("mon_sp/m1234/attack"u8);
        check(phases.Phase == EnemyActionPhase.Unknown && !EnemySkillCatalog.TimelineEffect(phases).IsReleasedEffect &&
            !EnemySkillCatalog.TimelineSound(phases, EnemySoundBinding.Target).IsReleasedEffect,
            "Overlapping execution/hit templates invented unambiguous release timing");
        var mixedTypes = new EnemySkillCatalog(new[] {
            new EnemyActionDefinition(900, 0, 700, 0, 0, 5), new EnemyActionDefinition(901, 3, 701, 0, 0, 0) }, new[] {
            new EnemyTimelineDefinition(700, "mon_sp/[SKL_ID]/attack"),
            new EnemyTimelineDefinition(701, "mon_sp/m[SKL_ID]/attack") });
        var types = mixedTypes.TimelineKey("mon_sp/m1234/attack"u8);
        check(types.IsSkill && types.Phase == EnemyActionPhase.Execution && !types.HasAttackType &&
            EnemySkillCatalog.TimelineEffect(types).IsReleasedEffect &&
            !EnemySkillCatalog.TimelineSound(types, EnemySoundBinding.CasterOrigin).IsReleasedEffect,
            "First-use fallback discarded overlapping setup metadata and enabled a caster-origin sound");

        var priority = new EnemySkillCatalog(new[] {
            new EnemyActionDefinition(900, 0, 700, 0, 0, 5), new EnemyActionDefinition(7, 1, 701, 0, 0, 1) }, new[] {
            new EnemyTimelineDefinition(700, "mon_sp/[SKL_ID]/shared"),
            new EnemyTimelineDefinition(701, "mon_sp/m1234/shared"),
            new EnemyTimelineDefinition(702, "mon_sp/m2222/shared") });
        check(priority.TimelineKey("mon_sp/m1234/shared"u8).Kind == EnemyActionKind.AutoAttack &&
            priority.TimelineKey("mon_sp/m2222/shared"u8).Kind == EnemyActionKind.Unknown,
            "Generic template fallback overrode an exact native auto-attack or unknown literal key");
        var actor = new GameAudioLayout.PlayerIdentity(200, 2000, 20000);
        check(priority.ObserveCast(actor, 2000, 900, 1) &&
            priority.TimelineKey("mon_sp/m1234/shared"u8, actor).IsSkill &&
            priority.TimelineKey("mon_sp/m1234/shared"u8, actor).TimelineId == 700,
            "Global literal/template metadata overrode this actor's unambiguous observed action");
    }

    private static void CheckAmbiguity(Action<bool, string> check)
    {
        var catalog = new EnemySkillCatalog(new[] {
            new EnemyActionDefinition(900, 0, 700, 1875, 130, 5),
            new EnemyActionDefinition(7, 1, 701, 1875, 0, 1),
        }, new[] {
            new EnemyTimelineDefinition(700, "mon_sp/[SKL_ID]/shared"),
            new EnemyTimelineDefinition(701, "mon_sp/[SKL_ID]/shared"),
            new EnemyTimelineDefinition(1875, "fixture/shared-hit"), new EnemyTimelineDefinition(130, "fixture/cast"),
        });
        check(!catalog.TimelineKey("mon_sp/[SKL_ID]/shared"u8).IsSkill && !catalog.TimelineKey("fixture/shared-hit"u8).IsSkill,
            "Globally shared auto-attack/Category 0 timelines were assumed to be skill-only");
        var skillActor = new GameAudioLayout.PlayerIdentity(200, 2000, 20000);
        var autoActor = new GameAudioLayout.PlayerIdentity(300, 3000, 30000);
        check(catalog.ObserveCast(skillActor, 2000, 900, 1) &&
            catalog.TimelineKey("mon_sp/m1234/shared"u8, skillActor).IsSkill,
            "Actor-specific first-cast evidence could not resolve an otherwise globally shared template");
        autoActor.ObserveAction(7);
        check(catalog.TimelineKey("mon_sp/m1234/shared"u8, autoActor).Kind == EnemyActionKind.AutoAttack,
            "Another actor inherited the newly learned skill instead of its own auto-attack history");
        skillActor.ObserveAction(7);
        var mixed = catalog.TimelineKey("mon_sp/m1234/shared"u8, skillActor);
        check(!mixed.IsSkill && !EnemySkillCatalog.TimelineSound(mixed, EnemySoundBinding.Target).IsReleasedEffect,
            "A single actor's own mixed auto-attack/skill evidence was guessed to be a released attack");
        var phases = new EnemySkillCatalog(new[] { new EnemyActionDefinition(900, 0, 700, 701, 0, 5),
            new EnemyActionDefinition(901, 0, 701, 0, 0, 5) }, new[] {
            new EnemyTimelineDefinition(700, "fixture/end"), new EnemyTimelineDefinition(701, "fixture/mixed-phase") });
        var phaseActor = new GameAudioLayout.PlayerIdentity(400, 4000, 40000);
        phases.ObserveCast(phaseActor, 4000, 900, 1); phases.ObserveCast(phaseActor, 4000, 901, 1);
        var ambiguous = phases.TimelineKey("fixture/mixed-phase"u8, phaseActor);
        check(ambiguous.IsSkill && ambiguous.Phase == EnemyActionPhase.Unknown &&
            !EnemySkillCatalog.TimelineSound(ambiguous, EnemySoundBinding.CasterOrigin).IsReleasedEffect,
            "Mixed execution/hit evidence became released-only after Category 0 recognition");
    }

    private static void CheckFirstUseEffectOwnership(Action<bool, string> check)
    {
        // Captured Byakko execution: known skill presentation, no attack type,
        // no cast/result history, and delayed audio from its exact C012 effect.
        var catalog = new EnemySkillCatalog(new[] {
            new EnemyActionDefinition(10241, 3, 4561, 3115, 0) }, new[] {
            new EnemyTimelineDefinition(4561, "mon_sp/[SKL_ID]/show/mon_sp001"),
            new EnemyTimelineDefinition(3115, "normal_hit/fastest_hit") });
        var caster = new GameAudioLayout.PlayerIdentity(200, 2000, 20000);
        var boss = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss);
        var execution = EnemySkillCatalog.TimelineSound(
            catalog.TimelineKey("mon_sp/m0454/show/mon_sp001"u8, caster), EnemySoundBinding.Unknown);
        var effects = new EnemyEffectOwnership();
        effects.Observe(100, 1000, caster, execution);
        var released = effects.Resolve(100, 1000, caster);
        check(EmptyHistory(caster) && !execution.HasAttackType && released.IsReleasedEffect &&
            catalog.ClassifySound(boss, caster, 1, default, released).IsReleasedEffect,
            "First-use model-template evidence did not reach the exact delayed C012 boss-effect route");
        check(!catalog.ClassifySound(boss, caster, 2, default, released).IsReleasedEffect &&
            !effects.Resolve(100, 1001, caster).IsReleasedEffect &&
            !effects.Resolve(100, 1000, new GameAudioLayout.PlayerIdentity(200, 2000, 20000)).IsReleasedEffect,
            "First-use model evidence weakened body-sound, effect-instance, or actor-identity isolation");
    }

    private static void CheckAllocation(Action<bool, string> check)
    {
        var catalog = CapturedCatalog(); var actor = new GameAudioLayout.PlayerIdentity(200, 2000, 20000);
        for (var i = 0; i < 2000; i++)
        {
            catalog.ObserveCast(actor, 2000, 8519, 1); catalog.TimelineKey("mon_sp/m0555/mon_sp001"u8, actor);
            catalog.TimelineKey("mon_sp/m0555/mon_sp001"u8);
        }
        var before = GC.GetAllocatedBytesForCurrentThread(); var matched = 0;
        for (var i = 0; i < 10000; i++)
            if (catalog.ObserveCast(actor, 2000, 8519, 1) && !catalog.ObserveCast(actor, 2001, 8542, 1) &&
                catalog.Action(8519, 1).IsSkill && catalog.TimelineKey("mon_sp/m0555/mon_sp001"u8, actor).IsSkill &&
                catalog.TimelineKey("mon_sp/m0555/mon_sp001"u8).IsSkill) matched++;
        check(matched == 10000 && GC.GetAllocatedBytesForCurrentThread() == before,
            "Cast observation or uncategorized skill/template lookup allocated on the repeated native callback path");
    }
}
