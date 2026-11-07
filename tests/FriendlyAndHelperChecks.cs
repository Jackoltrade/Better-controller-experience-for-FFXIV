// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;

internal static unsafe class FriendlyAndHelperChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var root = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Regular,
            NameId: 9140, BaseId: 11392);
        var helper = new GameAudioLayout.EnemyActor(210, 2100, 21000, 22, EnemySoundKind.Boss,
            EnemyActorRole.BossMechanic, NameId: 9140, BaseId: 9020);
        var selected = new GameAudioLayout.EnemyActor[8];
        var candidates = new[] { helper, root };
        check(GameAudioLayout.SelectEnemyActors(candidates, selected) == 2 &&
            selected[0].Kind == EnemySoundKind.Regular && selected[0].Role == EnemyActorRole.BossMechanic,
            "A dedicated regular-enemy effect helper failed to inherit its matching live enemy category");
        check(GameAudioLayout.SelectEnemyActors(new[] { helper }, selected) == 0 &&
            GameAudioLayout.SelectEnemyActors(new[] { helper, root with { NameId = 1 } }, selected) == 1 &&
            GameAudioLayout.SelectEnemyActors(new[] { helper, root with { Kind = EnemySoundKind.None } }, selected) == 0,
            "An orphaned, unrelated or non-enemy anchor registered an effect helper");
        var boss = root with { Actor = 220, EntityId = 2200, ObjectIndex = 24, Kind = EnemySoundKind.Boss };
        check(GameAudioLayout.SelectEnemyActors(new[] { helper, root, boss }, selected) == 3 &&
            selected[0].Kind == EnemySoundKind.Boss &&
            GameAudioLayout.SelectEnemyActors(new[] { helper, boss, root }, selected) == 3 &&
            selected[0].Kind == EnemySoundKind.Boss,
            "A matching regular actor displaced an independently proven boss anchor based on roster order");

        var remembered = new BossEncounterState();
        remembered.Update(100, 700, 1000, false, false);
        candidates = new[] { root };
        remembered.SelectRoster(candidates, selected);
        remembered.Update(100, 700, 1000, true, true);
        candidates = new[] { helper, root };
        check(remembered.SelectRoster(candidates, selected) == 2 && selected[0].Kind == EnemySoundKind.Regular &&
            selected[1].Kind == EnemySoundKind.Regular,
            "A fresh helper of remembered trash was promoted to the boss category during boss combat");
        candidates = new[] { helper, root, boss };
        check(remembered.SelectRoster(candidates, selected) == 3 && selected[0].Kind == EnemySoundKind.Boss,
            "Regular helper memory displaced an independently recognized matching boss");
        var fresh = new BossEncounterState();
        fresh.Update(100, 700, 1000, true, true);
        candidates = new[] { helper, root };
        check(fresh.SelectRoster(candidates, selected) == 2 && selected[0].Kind == EnemySoundKind.Boss &&
            selected[1].Kind == EnemySoundKind.Boss,
            "A new boss performer and its helper were treated as previously remembered trash");

        var friendly = root with { Actor = 300, EntityId = 3000, NameId = 9131, BaseId = 11366,
            Kind = EnemySoundKind.Boss, Role = EnemyActorRole.Unclassified };
        var friendlyChild = helper with { Actor = 310, EntityId = 3100, OwnerId = friendly.EntityId };
        var state = new BossEncounterState();
        candidates = new[] { friendlyChild, root, friendly, helper };
        var kept = state.ExcludePlayerOwned(candidates, new[] { friendly.EntityId });
        check(kept == 2 && candidates[0] == root && candidates[1] == helper,
            "Friendly exclusion failed to remove an allied NPC and its descendant without removing enemy effects");
        state.Update(100, 700, 1000, true, true);
        check(state.SelectRoster(candidates.AsSpan(0, kept), selected) == 2 &&
            Array.FindIndex(selected, 0, 2, a => a.EntityId == friendly.EntityId || a.EntityId == friendlyChild.EntityId) == -1,
            "Boss-encounter promotion reintroduced an excluded ally");

        var pool = (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed(
            (nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        try
        {
            var ownership = new GameAudioLayout.PlayerSoundOwnership(pool);
            ownership.SetPlayer(100, 1000, 10000);
            candidates = new[] { helper, root };
            var count = GameAudioLayout.SelectEnemyActors(candidates, selected);
            ownership.SetEnemies(selected.AsSpan(0, count), 700);
            var identity = ownership.ResolveCaster(helper.Actor, 0);
            check(identity != null, "The registered regular helper could not resolve its exact caster identity");
            pool->IsActive = 1; pool->SoundId = 123; pool->IsPositional = 1; pool->Position = new(4, 0, 3);
            var skill = new EnemyActionTag(EnemyActionKind.Skill, 18740, 1222, EnemyActionPhase.ReleasedEffect);
            ownership.Acquired(pool, identity, origin: 1, action: skill);
            check(ownership.Inspect(123, true, true).Enemy == EnemySoundKind.Regular,
                "A confirmed helper skill failed regular-enemy sound admission");
            foreach (var rejected in new[] { default(EnemyActionTag),
                new EnemyActionTag(EnemyActionKind.AutoAttack, 7), new EnemyActionTag(EnemyActionKind.HitReaction, 7) })
            {
                ownership.Acquired(pool, identity, origin: 1, action: rejected);
                check(ownership.Inspect(123, true, true).Enemy == EnemySoundKind.None,
                    "Registering regular helpers broadly admitted unknown, basic-attack or reaction sounds");
            }
            ownership.Acquired(pool, identity, origin: 1, action: skill);
            ownership.SetEnemies(ReadOnlySpan<GameAudioLayout.EnemyActor>.Empty, 700);
            check(ownership.Inspect(123, true, true).Enemy == EnemySoundKind.None,
                "An unregistered helper retained an old live sound attribution");
        }
        finally { NativeMemory.Free(pool); }

        candidates = new[] { helper, root };
        var bytes = AllocationChecks.MeasureAllocations(() => GameAudioLayout.SelectEnemyActors(candidates, selected),
            warmup: 2000, iterations: 10000);
        check(bytes == 0, $"Stable regular-helper selection allocated {bytes} bytes");
    }
}
