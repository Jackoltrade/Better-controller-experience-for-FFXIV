// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;

internal static unsafe class EnemyMechanicRoutingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckEncounterRouting(check);
        CheckPlayerOwnedExclusion(check);
    }

    private static readonly EnemyActionTag Skill = new(EnemyActionKind.Skill, TimelineId: 10000,
        Phase: EnemyActionPhase.ReleasedEffect);

    private static void Reserve(GameAudioLayout.PlayerSoundOwnership ownership, GameAudioLayout.SoundData* sound,
        GameAudioLayout.PlayerIdentity? caster, int slot, byte origin, EnemyActionTag tag)
    {
        ownership.Acquired(sound, caster, origin, caster?.Actor ?? 0, action: tag);
        sound->IsActive = 1; sound->SoundId = -100 - slot; sound->ResourceHandle = 999;
        sound->SoundNumber = 0; sound->IsPositional = 1; sound->Position = new Vector3(2, 0, 0);
        ownership.LinkPlayback(sound->SoundId, 500 + slot);
    }

    private static void CheckEncounterRouting(Action<bool, string> check)
    {
        var pool = (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed(
            (nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        try
        {
            var ownership = new GameAudioLayout.PlayerSoundOwnership(pool);
            ownership.SetPlayer(100, 1000, 10000);
            var settings = new HapticSettings(BossEnabled: true, EnemyStrength: 0.6f);
            // The caller supplies the active instance condition. No party-size
            // argument or eight-player-only promotion exists in this path.
            foreach (var (size, duty) in new[] { (4, 42u), (8, 263u), (24, 912u), (72, 1137u) })
            {
                var state = new BossEncounterState();
                var territory = (uint)(100 + size);
                state.Update(territory, duty, 1000, false, false);
                var regular = new GameAudioLayout.EnemyActor(300, 3000, 30000, 30, EnemySoundKind.Regular,
                    NameId: 91000, BaseId: 92000, OwnerId: 0xE0000000);
                check(state.RememberRegular(regular), "A pre-boss regular enemy was not remembered");
                var fresh = regular with { Actor = 400, EntityId = 4000, ObjectIndex = 40, NameId = 91001, BaseId = 92001 };
                var helper = fresh with { Actor = 500, EntityId = 5000, ObjectIndex = 50,
                    Kind = EnemySoundKind.Boss, Role = EnemyActorRole.BossMechanic, NameId = 91002, BaseId = 92002 };
                var summon = helper with { Actor = 600, EntityId = 6000, ObjectIndex = 60,
                    Role = EnemyActorRole.BossSummon, OwnerId = helper.EntityId, NameId = 91003, BaseId = 92003 };
                var neutral = fresh with { Actor = 700, EntityId = 7000, ObjectIndex = 70,
                    Kind = EnemySoundKind.None, Role = EnemyActorRole.Unclassified, NameId = 91004, BaseId = 92004 };
                var baseline = new[] { regular, fresh, helper, summon, neutral };
                state.Update(territory, duty, 1000, true, true);
                check(state.Active, "An active supported instance depended on an eight-player party definition");
                var candidates = new GameAudioLayout.EnemyActor[baseline.Length];
                for (var i = 0; i < candidates.Length; i++) candidates[i] = state.Classify(baseline[i]);
                check(candidates[0] == regular && state.WasRegular(candidates[0]),
                    "Entering boss combat promoted a previously marked regular enemy");
                for (var i = 1; i < candidates.Length; i++)
                    check(candidates[i].Kind == EnemySoundKind.Boss && candidates[i].Role == EnemyActorRole.BossEncounter &&
                        candidates[i].Actor == baseline[i].Actor && candidates[i].EntityId == baseline[i].EntityId &&
                        candidates[i].OwnerId == baseline[i].OwnerId,
                        "An unseen mechanic/neutral/summon NPC lost its exact identity or boss encounter classification");
                var selected = new GameAudioLayout.EnemyActor[candidates.Length];
                var count = GameAudioLayout.SelectEnemyActors(candidates, selected);
                check(count == candidates.Length, "The selector dropped a classified encounter NPC");
                ownership.SetEnemies(selected.AsSpan(0, count), territory);
                for (var i = 1; i < candidates.Length; i++)
                {
                    var identity = ownership.ResolveTimelineCaster(candidates[i].ObjectIndex, 0);
                    check(identity != null && identity.Actor == baseline[i].Actor,
                        "A classified mechanic's timeline index resolved to another actor");
                    foreach (var origin in new byte[] { 1, 2, 3 })
                        foreach (var tag in new[] { default(EnemyActionTag), new EnemyActionTag(EnemyActionKind.AutoAttack, 7),
                            new EnemyActionTag(EnemyActionKind.Skill, TimelineId: 130, Phase: EnemyActionPhase.Caster) })
                        {
                            Reserve(ownership, pool + i, identity, i, origin, tag);
                            check(ownership.EnemyKindOf(500 + i) == EnemySoundKind.Boss && !ownership.Contains(500 + i) &&
                                ownership.TryGetEnemyPosition(500 + i, out var position) && position == new Vector3(2, 0, 0) &&
                                settings.AdditionalGain(false, 2, 0, false, ownership.EnemyKindOf(500 + i)) == 0.6f,
                                "An unseen NPC's raw/auto/caster/body SFX did not reach the boss channel");
                        }
                }
                var regularIdentity = ownership.ResolveCaster(regular.Actor, 0);
                Reserve(ownership, pool, regularIdentity, 0, 1, Skill);
                check(ownership.EnemyKindOf(500) == EnemySoundKind.Regular &&
                    settings.AdditionalGain(false, 2, 0, false, ownership.EnemyKindOf(500)) == 0,
                    "A sticky regular enemy ignored its saved regular-enemy switch during boss combat");

                state.Update(territory, duty, 1000, true, false);
                check(!state.Active && state.Classify(fresh) == fresh && state.Classify(neutral) == neutral,
                    "Ending the encounter retained a broad NPC category instead of the baseline classification");
                var outside = new[] { state.Classify(regular), state.Classify(fresh) };
                count = GameAudioLayout.SelectEnemyActors(outside, selected);
                ownership.SetEnemies(selected.AsSpan(0, count), territory);
                for (var i = 1; i < baseline.Length; i++)
                    check(ownership.EnemyKindOf(500 + i) == EnemySoundKind.None,
                        "Ending the encounter retained a queued broad-category sound lease");
                var outsideIdentity = ownership.ResolveCaster(fresh.Actor, 0);
                foreach (var tag in new[] { default(EnemyActionTag), new EnemyActionTag(EnemyActionKind.AutoAttack, 7) })
                {
                    Reserve(ownership, pool + 1, outsideIdentity, 1, 3, tag);
                    check(ownership.EnemyKindOf(501) == EnemySoundKind.None,
                        "Legacy regular-enemy behavior admitted raw or basic-attack audio after the encounter");
                }
                Reserve(ownership, pool + 1, outsideIdentity, 1, 3, Skill);
                check(ownership.EnemyKindOf(501) == EnemySoundKind.Regular &&
                    settings.AdditionalGain(false, 2, 0, false, ownership.EnemyKindOf(501)) == 0,
                    "A normal enemy outside the encounter stopped using the regular skills-only route");
            }
        }
        finally { NativeMemory.Free(pool); }
    }

    private static void CheckPlayerOwnedExclusion(Action<bool, string> check)
    {
        var state = new BossEncounterState();
        state.Update(100, 263, 1000, true, true);
        var npc = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Regular);
        var player = npc with { Actor = 300, EntityId = 3000, ObjectIndex = 30 };
        var pet = npc with { Actor = 400, EntityId = 4000, ObjectIndex = 40, OwnerId = player.EntityId };
        var nested = npc with { Actor = 500, EntityId = 5000, ObjectIndex = 50, OwnerId = pet.EntityId };
        // Reverse ownership order requires the fixed-point owner-chain exclusion,
        // rather than accidentally succeeding only for parent-before-child input.
        var candidates = new[] { nested, pet, player, npc };
        var kept = state.ExcludePlayerOwned(candidates, new[] { 1000u, player.EntityId });
        check(kept == 1 && candidates[0] == npc, "A player or player-owned NPC descendant entered encounter classification");
        candidates[0] = state.Classify(candidates[0]);
        var selected = new GameAudioLayout.EnemyActor[1];
        var count = GameAudioLayout.SelectEnemyActors(candidates.AsSpan(0, kept), selected);
        var ownership = new GameAudioLayout.PlayerSoundOwnership(null);
        ownership.SetPlayer(100, 1000, 10000);
        ownership.SetEnemies(selected.AsSpan(0, count), 100);
        check(count == 1 && ownership.ResolveCaster(npc.Actor, 0) != null &&
            ownership.ActorOf(ownership.Player) == null && ownership.ResolveCaster(player.Actor, 0) == null &&
            ownership.ResolveCaster(pet.Actor, 0) == null && ownership.ResolveCaster(nested.Actor, 0) == null,
            "Filtered players/pets were still registered as boss audio identities");
    }
}
