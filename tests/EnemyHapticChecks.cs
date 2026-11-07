// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static unsafe class EnemyHapticChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckBossHelpers(check);
        CheckSummons(check);
        CheckNecrobombs(check);
        CheckOwnership(check);
        CheckVoiceOwnershipSnapshot(check);
        CheckRouting(check);
        CheckSharedSpatialSettings(check);
        check(EnemyBossCatalog.IsBoss(537, 7225) && EnemyBossCatalog.IsBoss(538, 7225) &&
            !EnemyBossCatalog.IsBoss(4, 7225) && !EnemyBossCatalog.IsBoss(0, 7225) && !EnemyBossCatalog.IsBoss(537, 0),
            "Boss classification must use both the duty and caster's NPC name, not the target");
    }

    private static void CheckNecrobombs(Action<bool, string> check)
    {
        check(EnemyBossCatalog.IsBoss(737, 9508), "Spectral Necromancer is missing from the recorded duty");
        check(BossMechanics.OwnerName(737, 9512, 12030) == 9511 && EnemyBossCatalog.IsBoss(737, 9511) &&
            BossMechanics.OwnerName(738, 9512, 12030) == 0 && BossMechanics.OwnerName(737, 9512, 12031) == 0,
            "The third boss's rubble association was missing or widened beyond the recorded NPC entry");
        var root = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss, NameId: 9508, BaseId: 11761);
        var candidates = new GameAudioLayout.EnemyActor[9]; var selected = new GameAudioLayout.EnemyActor[9];
        var catalog = new EnemySkillCatalog(new[] { new EnemyActionDefinition(7, 1, uint.MaxValue, 0, 0) }, Array.Empty<EnemyTimelineDefinition>());
        for (var i = 0; i < 8; i++)
        {
            uint name = i < 4 ? 9510u : 9509u, npcBase = (uint)(11762 + i);
            var owner = BossMechanics.OwnerName(737, name, npcBase);
            check(owner == 9508 && BossMechanics.OwnerName(738, name, npcBase) == 0 &&
                BossMechanics.OwnerName(737, name == 9509 ? 9510u : 9509u, npcBase) == 0,
                "An explosive summon was missing or its association leaked across duty/NPC variants");
            candidates[i] = new(300 + i, (uint)(3000 + i), 0, 30 + i * 2, EnemySoundKind.Regular,
                EnemyActorRole.Combatant, name, npcBase, 0xE0000000, owner);
        }
        candidates[8] = root;
        var count = GameAudioLayout.SelectEnemyActors(candidates, selected);
        check(count == 9, "The second boss's eight recorded summon entries did not resolve");
        for (var i = 0; i < 8; i++)
        {
            var source = selected[i];
            var identity = new GameAudioLayout.PlayerIdentity(source.Actor, source.EntityId, source.DrawObject);
            identity.ObserveAction(7); // The captured second variant auto-attacks before exploding.
            check(source.Kind == EnemySoundKind.Boss && source.Role == EnemyActorRole.BossSummon &&
                catalog.ClassifySound(source, identity, 1, default, default).IsReleasedEffect,
                "A summoned explosion remained in the disabled regular-enemy channel");
            check(!catalog.ClassifySound(source, identity, 2, default, default).IsReleasedEffect &&
                !catalog.ClassifySound(source, identity, 1, new(EnemyActionKind.AutoAttack, 7), default).IsReleasedEffect,
                "An explosive-summon association admitted body sounds or a confirmed basic attack");
            check(!catalog.ClassifySound(source with { BossOwnerNameId = 0 }, identity, 1, default, default).IsReleasedEffect,
                "Verified-mechanic handling disabled the unknown-AVFX guard for ordinary owned fighters");
        }
        GameAudioLayout.SelectEnemyActors(candidates.AsSpan(0, 8), selected);
        check(Array.TrueForAll(selected[..8], a => a.Kind == EnemySoundKind.Regular && a.Role == EnemyActorRole.Combatant),
            "Removing the second boss left its summons in the boss channel");
        candidates[8] = root with { NameId = 9505 };
        GameAudioLayout.SelectEnemyActors(candidates, selected);
        check(selected[0].Kind == EnemySoundKind.Regular && selected[7].Kind == EnemySoundKind.Regular,
            "The first boss incorrectly anchored the second boss's summons");
        check(BossMechanics.OwnerName(737, 9509, 11765) == 0 && BossMechanics.OwnerName(737, 9510, 11766) == 0 &&
            BossMechanics.OwnerName(737, 9509, 11770) == 0 && BossMechanics.OwnerName(737, 9507, 11889) == 9505,
            "Bomb coverage widened to unrelated NPC bases or changed the verified knife association");
    }

    private static void CheckSummons(Action<bool, string> check)
    {
        check(BossMechanics.OwnerName(737, 9507, 11889) == 9505 &&
            BossMechanics.OwnerName(738, 9507, 11889) == 0 && BossMechanics.OwnerName(737, 9508, 11889) == 0 &&
            BossMechanics.OwnerName(737, 9507, 11890) == 0,
            "The recorded delayed knife mechanic was not scoped to its exact duty and NPC entries");
        var root = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss, NameId: 9505, BaseId: 11756);
        var knife = new GameAudioLayout.EnemyActor(300, 3000, 0, 30, EnemySoundKind.Boss, EnemyActorRole.BossSummon,
            9507, 11889, 0xE0000000, BossMechanics.OwnerName(737, 9507, 11889));
        var selected = new GameAudioLayout.EnemyActor[8];
        var knives = new[] { knife, knife with { Actor = 310, EntityId = 3100, ObjectIndex = 32 },
            knife with { Actor = 320, EntityId = 3200, ObjectIndex = 34 }, knife with { Actor = 330, EntityId = 3300, ObjectIndex = 36 }, root };
        check(GameAudioLayout.SelectEnemyActors(knives, selected) == 5 && selected[0].Role == EnemyActorRole.BossSummon,
            "The recorded four non-hostile knife emitters failed to inherit the boss channel");
        check(GameAudioLayout.SelectEnemyActors(new[] { knife }, selected) == 0 &&
            GameAudioLayout.SelectEnemyActors(new[] { knife, root with { NameId = 9999 } }, selected) == 1,
            "A mapped mechanic without its matching live boss was accepted");
        var owned = knife with { NameId = 555, BaseId = 666, OwnerId = root.EntityId, BossOwnerNameId = 0 };
        check(GameAudioLayout.SelectEnemyActors(new[] { owned, root }, selected) == 2 &&
            selected[0].Kind == EnemySoundKind.Boss && selected[0].Role == EnemyActorRole.BossSummon,
            "A native-owned boss summon required a per-encounter NPC entry");
        check(GameAudioLayout.SelectEnemyActors(new[] { owned with { OwnerId = 1234 }, root }, selected) == 1 &&
            GameAudioLayout.SelectEnemyActors(new[] { knife with { OwnerId = 1234 }, root }, selected) == 1,
            "Unrelated native ownership was ignored or overridden by an encounter fallback");
        var regular = owned with { Kind = EnemySoundKind.Regular, Role = EnemyActorRole.Combatant };
        check(GameAudioLayout.SelectEnemyActors(new[] { regular, root }, selected) == 2 && selected[0].Kind == EnemySoundKind.Boss,
            "A hostile native-owned boss summon stayed in the disabled regular-enemy channel");
        check(GameAudioLayout.SelectEnemyActors(new[] { regular with { OwnerId = 1234 }, root }, selected) == 2 &&
            selected[0].Kind == EnemySoundKind.Regular && selected[0].Role == EnemyActorRole.Combatant,
            "An unrelated combatant was included in the boss channel");
        var catalog = new EnemySkillCatalog(Array.Empty<EnemyActionDefinition>(), Array.Empty<EnemyTimelineDefinition>());
        var identity = new GameAudioLayout.PlayerIdentity(knife.Actor, knife.EntityId, knife.DrawObject);
        var tag = catalog.ClassifySound(knife, identity, 1, default, default);
        check(tag.IsReleasedEffect && tag.Phase == EnemyActionPhase.BossMechanic && tag.ActionId == 0,
            "A linked delayed summon effect was lost or assigned an invented action ID");
        check(!catalog.ClassifySound(knife, identity, 2, default, default).IsReleasedEffect &&
            !catalog.ClassifySound(knife, identity, 1, new(EnemyActionKind.AutoAttack, 7), default).IsReleasedEffect,
            "Summon attribution admitted character audio or confirmed basic attacks");
        var pool = (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed((nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        try
        {
            var ownership = new GameAudioLayout.PlayerSoundOwnership(pool); ownership.SetPlayer(100, 1000, 10000);
            var count = GameAudioLayout.SelectEnemyActors(knives, selected); ownership.SetEnemies(selected.AsSpan(0, count), 737);
            pool->IsActive = 1; pool->SoundId = 123; pool->IsPositional = 1; pool->Position = new(4, 0, 3);
            ownership.Acquired(pool, ownership.ResolveCaster(knife.Actor, 0), origin: 1, action: tag);
            check(ownership.EnemyKindOf(123) == EnemySoundKind.Boss && ownership.TryGetEnemyPosition(123, out var position) &&
                position == new Vector3(4, 0, 3) && !ownership.Contains(123),
                "The delayed effect lost its real position or contaminated the player's SFX channel");
            count = GameAudioLayout.SelectEnemyActors(knives.AsSpan(0, 4), selected);
            ownership.SetEnemies(selected.AsSpan(0, count), 737);
            check(ownership.EnemyKindOf(123) == EnemySoundKind.None,
                "Despawning the boss left a stale summon voice attributed to it");
        }
        finally { NativeMemory.Free(pool); }
        Action select = () => GameAudioLayout.SelectEnemyActors(knives, selected);
        var allocated = AllocationChecks.MeasureAllocations(select, warmup: 2000, iterations: 10000);
        check(allocated == 0, $"Summon resolution allocated {allocated} bytes during framework publication");
    }

    private static void CheckBossHelpers(Action<bool, string> check)
    {
        // Captured 拉格 emitters share the boss name ID, but use a different base entry.
        var root = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss,
            NameId: 10256, BaseId: 13294);
        var helper = root with { Actor = 210, EntityId = 2100, ObjectIndex = 22, Role = EnemyActorRole.BossMechanic, BaseId = 9020 };
        var selected = new GameAudioLayout.EnemyActor[8];
        check(EnemyBossCatalog.IsBoss(783, 10256), "The recorded boss is missing from its actual duty mapping");
        var count = GameAudioLayout.SelectEnemyActors(new[] { helper, root }, selected);
        check(count == 2 && selected[0] == helper && selected[1] == root,
            "Matching boss emitters depended on object-table order or were discarded as non-combatants");
        var volley = new GameAudioLayout.EnemyActor[14];
        for (var i = 0; i < 13; i++) volley[i] = helper with { Actor = 300 + i, EntityId = (uint)(3000 + i), ObjectIndex = 40 + i * 2 };
        volley[13] = root;
        check(GameAudioLayout.SelectEnemyActors(volley, new GameAudioLayout.EnemyActor[14]) == 14,
            "The recorded 13-emitter volley was rejected because the boss has a different base ID");
        foreach (var invalid in new[] { helper with { NameId = 1 },
            helper with { NameId = 0 }, helper with { BaseId = 0 }, helper with { EntityId = 0 },
            helper with { EntityId = 0xE0000000 }, helper with { Actor = 0 }, helper with { Kind = EnemySoundKind.Regular } })
            check(GameAudioLayout.SelectEnemyActors(new[] { invalid, root }, selected) == 1 && selected[0] == root,
                "An unrelated, invalid or unidentifiable helper acquired boss attribution");
        foreach (var invalid in new[] { root with { EntityId = 0 },
            root with { EntityId = 0xE0000000 }, root with { Actor = 0 }, root with { Role = EnemyActorRole.BossMechanic } })
        {
            count = GameAudioLayout.SelectEnemyActors(new[] { helper, invalid }, selected);
            check(Array.IndexOf(selected, helper, 0, count) == -1, "A helper without a real enemy anchor was registered");
        }
        check(GameAudioLayout.SelectEnemyActors(new[] { helper }, selected) == 0 &&
            GameAudioLayout.SelectEnemyActors(new[] { root, helper }, Span<GameAudioLayout.EnemyActor>.Empty) == 0,
            "Orphaned helpers or a full output span were not handled safely");
    }

    private static void CheckOwnership(Action<bool, string> check)
    {
        var pool = (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed((nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        try
        {
            var ownership = new GameAudioLayout.PlayerSoundOwnership(pool);
            ownership.SetPlayer(100, 1000, 10000);
            var actors = new[] { new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss),
                new GameAudioLayout.EnemyActor(300, 3000, 30000, 30, EnemySoundKind.Regular) };
            ownership.SetEnemies(actors, 777);
            var boss = ownership.ResolveCaster(200, 0);
            var regular = ownership.ResolveTimelineCaster(30, 1);
            boss!.SetDrawChildren(new nint[] { 20001 });
            check(boss != null && regular != null && !ReferenceEquals(boss, regular), "Enemy casters were not registered separately");
            check(ReferenceEquals(ownership.ResolveCaster(0, 20001), boss) && ownership.ResolveCaster(999, 20001) == null,
                "Enemy weapon models lost ownership or overrode another explicit caster");
            check(ReferenceEquals(ownership.ResolveCaster(100, 0), ownership.Player) &&
                ReferenceEquals(ownership.ResolveTimelineCaster(1, 1), ownership.Player), "Enemy roster changed player attribution");
            check(ownership.ResolveCaster(999, 20000) == null && ownership.ResolveCaster(0, 20000) == boss &&
                ownership.ResolveCaster(0, 0) == null && ownership.ResolveTimelineCaster(-1, 1) == null &&
                ownership.ResolveTimelineCaster(99, 1) == null, "Unknown/friendly actors acquired enemy attribution");
            for (var i = 0; i < 4; i++)
            {
                pool[i].IsActive = 1; pool[i].SoundId = 1001 + i;
                pool[i].IsPositional = 1; pool[i].Position = new Vector3(2, 0, 0);
            }
            ownership.Acquired(pool, ownership.Player);
            pool[1].SoundId = -11;
            ownership.Acquired(pool + 1, boss, sourceActor: 200, action: new(EnemyActionKind.Skill, 39021, Phase: EnemyActionPhase.ReleasedEffect));
            ownership.Acquired(pool + 2, regular, timelineOwnerIndex: 30, action: new(EnemyActionKind.Skill, 39021, Phase: EnemyActionPhase.ReleasedEffect));
            // A teammate's hit at the same boss location has no enemy caster tag.
            ownership.Acquired(pool + 3, ownership.ResolveCaster(999, 0), sourceActor: 999);
            pool[4].IsActive = 1; pool[4].SoundId = 1005;
            ownership.Acquired(pool + 4, boss, sourceActor: 200);
            check(ownership.EnemyKindOf(1005) == EnemySoundKind.None,
                "Enemy caster ownership alone admitted a basic/unknown attack without skill evidence");
            ownership.Acquired(pool + 4, boss, action: new(EnemyActionKind.AutoAttack, 7));
            check(ownership.EnemyKindOf(1005) == EnemySoundKind.None,
                "A confirmed enemy auto-attack was accepted as a skill");
            ownership.Acquired(pool + 4, boss, action: new(EnemyActionKind.Skill));
            check(ownership.EnemyKindOf(1005) == EnemySoundKind.None,
                "An enemy tag without an action or timeline ID became confirmed skill evidence");
            ownership.Acquired(pool + 4, boss, action: new(EnemyActionKind.Skill, TimelineId: 3201, Phase: EnemyActionPhase.Caster));
            check(ownership.EnemyKindOf(1005) == EnemySoundKind.None,
                "The captured boss-body timeline was included by Boss skills");
            var helper = new GameAudioLayout.EnemyActor(400, 4000, 40000, 40, EnemySoundKind.Boss,
                EnemyActorRole.BossMechanic, 10256, 9020);
            ownership.SetEnemies(new[] { actors[0], actors[1], helper }, 777);
            var emitter = ownership.ResolveCaster(400, 0);
            ownership.Acquired(pool + 4, emitter, origin: 1, sourceActor: 400,
                action: new(EnemyActionKind.Skill, Phase: EnemyActionPhase.BossMechanic));
            var snapshot = Array.Find(ownership.Snapshot(), sound => sound.SoundId == 1005);
            check(ownership.EnemyKindOf(1005) == EnemySoundKind.Boss && !ownership.Contains(1005) &&
                ownership.ActorOf(emitter) == helper && snapshot.EnemyRole == EnemyActorRole.BossMechanic &&
                snapshot.NameId == 10256 && snapshot.BaseId == 9020,
                "Mechanic audio lost its separate boss ownership or diagnostic metadata");
            ownership.SetEnemies(actors, 777);
            check(ownership.EnemyKindOf(1005) == EnemySoundKind.None && ownership.ActorOf(emitter) == null,
                "A despawned helper retained boss feedback");
            ownership.Acquired(pool, ownership.Player, action: new(EnemyActionKind.AutoAttack, 7));
            check(ownership.Contains(1001), "Enemy-only filtering suppressed the player's existing auto-attack sounds");
            check(!ownership.LinkPlayback(-11, 1002) && ownership.EnemyKindOf(1002) == EnemySoundKind.Boss,
                "Enemy reservation/playback linking either failed or became player ownership");
            check(ownership.Contains(1001) && !ownership.Contains(1002) && !ownership.Contains(1003) &&
                ownership.EnemyKindOf(1001) == EnemySoundKind.None && ownership.EnemyKindOf(1003) == EnemySoundKind.Regular &&
                ownership.EnemyKindOf(1004) == EnemySoundKind.None, "Players, regular enemies and bosses contaminated each other's channels");
            check(ownership.TryGetEnemyPosition(1002, out var position) && position.X == 2 &&
                !ownership.TryGetPosition(1002, out _), "Enemy positions leaked into player lighting/haptic ownership");
            pool[1].IsPositional = 0;
            check(!ownership.TryGetEnemyPosition(1002, out _), "An unpositioned enemy sound invented a position");
            pool[1].IsPositional = 1; pool[1].Position.X = float.NaN;
            check(!ownership.TryGetEnemyPosition(1002, out _), "An invalid enemy position was accepted");
            pool[1].Position.X = 2;
            ownership.SetEnemies(actors, 777);
            check(ReferenceEquals(boss, ownership.ResolveCaster(200, 0)) && ownership.EnemyKindOf(1002) == EnemySoundKind.Boss,
                "Unchanged framework publication invalidated live audio ownership");
            ownership.SetEnemies(new[] { actors[1], actors[0] }, 777);
            check(ReferenceEquals(boss, ownership.ResolveCaster(200, 0)) && ownership.EnemyKindOf(1002) == EnemySoundKind.Boss,
                "Object-table ordering changed an existing caster identity");
            ownership.SetEnemies(actors.AsSpan(1), 777);
            check(ownership.EnemyKindOf(1002) == EnemySoundKind.None && ownership.EnemyKindOf(1003) == EnemySoundKind.Regular,
                "Despawning a boss failed to invalidate its sounds or invalidated another enemy");
            ownership.SetEnemies(actors, 777);
            check(!ReferenceEquals(boss, ownership.ResolveCaster(200, 0)) && ownership.EnemyKindOf(1002) == EnemySoundKind.None,
                "Reusing an actor address revived stale boss audio");
            ownership.Acquired(pool + 1, boss, action: new(EnemyActionKind.Skill, 39021, Phase: EnemyActionPhase.ReleasedEffect));
            ownership.LinkPlayback(-11, 1002);
            check(ownership.EnemyKindOf(1002) == EnemySoundKind.None, "A queued scope from a removed caster was trusted");
            ownership.Acquired(pool + 1, ownership.ResolveCaster(200, 0), action: new(EnemyActionKind.Skill, 39021, Phase: EnemyActionPhase.ReleasedEffect)); ownership.LinkPlayback(-11, 1002);
            ownership.SetEnemies(actors, 778);
            check(ownership.EnemyKindOf(1002) == EnemySoundKind.None && ownership.EnemyKindOf(1003) == EnemySoundKind.None,
                "Changing territory retained enemy ownership");
            ownership.Acquired(pool + 1, ownership.ResolveCaster(200, 0), action: new(EnemyActionKind.Skill, 39021, Phase: EnemyActionPhase.ReleasedEffect)); ownership.LinkPlayback(-11, 1002);
            actors[0] = actors[0] with { EntityId = 2001 };
            ownership.SetEnemies(actors, 778);
            check(ownership.EnemyKindOf(1002) == EnemySoundKind.None, "A new entity at the same address inherited old sound ownership");
            ownership.Acquired(pool + 1, ownership.ResolveCaster(200, 0), action: new(EnemyActionKind.Skill, 39021, Phase: EnemyActionPhase.ReleasedEffect)); ownership.LinkPlayback(-11, 1002);
            pool[1].SoundId = -12;
            check(ownership.EnemyKindOf(1002) == EnemySoundKind.None, "Reused native sound slot retained a stale playback link");
            pool[1].SoundId = -11;
            ownership.Acquired(pool + 1, null);
            check(ownership.EnemyKindOf(1002) == EnemySoundKind.None, "An unknown acquisition failed to clear the prior caster");
            ownership.SetPlayer(0, 0, 0);
            check(ownership.EnemyKindOf(1003) == EnemySoundKind.None && ownership.ResolveCaster(200, 0) == null,
                "Logging out retained the enemy roster");

            ownership.SetPlayer(100, 1000, 10000); ownership.SetEnemies(actors, 778);
            ownership.Acquired(pool + 2, ownership.ResolveCaster(300, 0), action: new(EnemyActionKind.Skill, 39021, Phase: EnemyActionPhase.ReleasedEffect));
            // Warm the complete measured path through tiered JIT before
            // attributing runtime setup allocations to the audio algorithm.
            for (var i = 0; i < 20000; i++)
            { ownership.SetEnemies(actors, 778); ownership.EnemyKindOf(1003); ownership.TryGetEnemyPosition(1003, out _); }
            var count = 0;
            Action publish = () =>
            {
                ownership.SetEnemies(actors, 778);
                if (ownership.EnemyKindOf(1003) == EnemySoundKind.Regular && ownership.TryGetEnemyPosition(1003, out _)) count++;
                ownership.ResolveCaster(200, 0); ownership.ResolveTimelineCaster(30, 1);
            };
            var allocated = AllocationChecks.MeasureAllocations(publish, warmup: 0, iterations: 10000);
            check(allocated == 0 && count == 10000,
                $"Steady-state enemy publication or audio attribution allocated {allocated} bytes");
        }
        finally { NativeMemory.Free(pool); }
    }

    private static int calls;
    private static float lastLeft, lastRight, lastGain;
    private static uint lastFrames, lastFormat;
    private static nint lastSamples;
    [UnmanagedCallersOnly]
    private static void Capture(nint destination, float* samples, uint frames, float gain, uint format)
    {
        calls++; lastLeft = samples[0]; lastRight = samples[1]; lastGain = gain;
        lastFrames = frames; lastFormat = format; lastSamples = (nint)samples;
    }

    private static void CheckRouting(Action<bool, string> check)
    {
        var requested = new HapticSettings(BossEnabled: true, EnemyStrength: 0.6f,
            Spatial: true, DistanceFalloff: true, AutomaticDistance: true,
            DistanceOrigin: HapticDistanceOrigin.Character, FollowImpact: true, ImpactBias: 1);
        check(requested.NeedsAudio && !requested.Enabled && !new HapticSettings().EnemiesEnabled,
            "Enemy haptics must be independently opt-in");
        check((requested with { EnemyStrength = float.NaN }).Normalize().EnemyStrength == 0.25f &&
            (requested with { EnemyStrength = 3 }).Normalize().EnemyStrength == 1, "Enemy strength is unbounded");
        check(requested.AdditionalGain(false, 2, 0, false, EnemySoundKind.Boss) == 0.6f &&
            requested.AdditionalGain(false, 2, 0, false, EnemySoundKind.Regular) == 0 &&
            requested.AdditionalGain(true, 2, 0, false, EnemySoundKind.Boss) == 0,
            "Boss toggle enabled regular enemies or player audio");
        foreach (var bus in new byte[] { 0, 1, 3, 6, 7, 9, 11, 20 })
            check(requested.AdditionalGain(false, bus, 0, false, EnemySoundKind.Boss) == 0,
                "Enemy haptics included non-combat audio");
        foreach (uint flags in new uint[] { 2, 4, 8, 14 })
            check(requested.AdditionalGain(false, 2, flags, false, EnemySoundKind.Boss) == 0,
                "Enemy haptics duplicated native vibration");
        check(requested.AdditionalGain(false, 2, 0, true, EnemySoundKind.Boss) == 0, "An existing native send was duplicated");
        check(requested.AdditionalGain(false, 5, 0, false, EnemySoundKind.Boss) == 0 &&
            requested.AdditionalGain(false, 5, 0, false, EnemySoundKind.Boss, arenaEffect: true) == 0.6f,
            "Arena effect evidence did not isolate environmental boss feedback from ordinary ambience");
        check(requested.AdditionalGain(true, 5, 0, false, EnemySoundKind.Boss, arenaEffect: true) == 0 &&
            requested.AdditionalGain(false, 5, 0, false, EnemySoundKind.Regular, arenaEffect: true) == 0 &&
            requested.AdditionalGain(false, 5, 0, false, arenaEffect: true) == 0 &&
            requested.AdditionalGain(false, 5, 0, false, EnemySoundKind.Boss, hitReaction: true, arenaEffect: true) == 0,
            "Arena evidence bypassed player, regular-enemy or hit-reaction ownership gates");
        foreach (uint flags in new uint[] { 2, 4, 8, 14 })
            check(requested.AdditionalGain(false, 5, flags, false, EnemySoundKind.Boss, arenaEffect: true) == 0,
                "Arena feedback duplicated native vibration-only or routed audio");
        check(requested.AdditionalGain(false, 5, 0, true, EnemySoundKind.Boss, arenaEffect: true) == 0 &&
            (requested with { BossEnabled = false }).AdditionalGain(false, 5, 0, false, EnemySoundKind.Boss, arenaEffect: true) == 0 &&
            !GameAudioLayout.IsCombatSound(5, 0),
            "Arena effects bypassed the boss switch or changed combat lighting's bus selection");

        var table = stackalloc nint[16]; new Span<nint>(table, 16).Clear();
        var mix = (nint)(delegate* unmanaged<nint, float*, uint, float, uint, void>)&Capture; table[15] = mix;
        var destination = stackalloc byte[0x160]; new Span<byte>(destination, 0x160).Clear();
        *(nint*)destination = (nint)table; *(int*)(destination + 0xE0) = 1;
        "Vibration\0"u8.CopyTo(new Span<byte>(destination + 0x13E, 10));
        var graph = stackalloc nint[15]; new Span<nint>(graph, 15).Clear(); graph[9] = (nint)destination;
        var output = new NativeHapticOutput(graph, mix, (nint)table, (nint)table + 16 * 8);
        var bed = new float[16]; bed[0] = bed[8] = 1;
        var ambisonic = new float[32]; ambisonic[0] = ambisonic[16] = 1;
        output.SetSpatialMatrices(bed, ambisonic);
        output.SetEnemyContext(true); output.Configure(requested, true, true);
        output.SetListener(new(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp(), new(0, 0, 60)));
        check(output.Active && output.EnemiesActive && output.WantsSpatial && !output.CurrentSettings.Enabled,
            "Enemy-only mode requires the Player SFX category or ignores the shared spatial settings");
        GameAudioLayout.SourceVoice voice = default; voice.SendCount = 1;
        var sends = (GameAudioLayout.VoiceSend*)((byte*)&voice + 0x940); sends[0].Destination = 123; sends[0].Gain = 1;
        var originalVoice = new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice)).ToArray();
        var pcm = stackalloc float[2048]; new Span<float>(pcm, 2048).Clear();
        for (var i = 0; i < 256; i++) pcm[i * 8] = 0.1f;
        calls = 0;
        var ground = output.Prepare(&voice, false, 5, 0, new(2, 0, 0), 1003, EnemySoundKind.Boss, arenaEffect: true);
        check(ground.Route == HapticRoute.Spatial && ground.Enemy == EnemySoundKind.Boss && ground.Gain == 0.6f &&
            ground.Spatial.Right == 1 && ground.Spatial.Left == 0 &&
            output.Prepare(&voice, false, 5, 0, new(2, 0, 0), enemy: EnemySoundKind.Boss).Destination == 0,
            "Known ground effects lost shared spatial routing or admitted ambient audio without evidence");
        check(output.Prepare(&voice, false, 5, 0, new(0, 0, 25), enemy: EnemySoundKind.Boss,
            arenaEffect: true).Route == HapticRoute.DistanceCutoff,
            "Ground effects bypassed the selected character-based distance cutoff");
        var frame = output.Prepare(&voice, false, 2, 0, new(2, 0, 0), 1002, EnemySoundKind.Boss);
        check(frame.Route == HapticRoute.Spatial && frame.Spatial.Distance == 2 && frame.Spatial.Left == 0 && frame.Spatial.Right == 1 &&
            frame.Gain == 0.6f && frame.Enemy == EnemySoundKind.Boss && !frame.Biased,
            "Enemy routing ignored the selected character origin or inherited player impact bias");
        output.Mix(frame, pcm, 2048, 0);
        check(calls == 1 && lastLeft == 0 && lastRight == 0.1f && lastGain == 0.6f && output.BossBlocks == 1,
            "Boss PCM did not follow the correct grip/strength");
        check(output.Prepare(&voice, false, 2, 0, new(2, 0, 0), enemy: EnemySoundKind.Regular).Destination == 0 &&
            output.Prepare(&voice, false, 2, 0, new(2, 0, 0)).Destination == 0, "Boss-only feedback accepted another/unknown caster");
        foreach (var distance in new[] { 0f, 3f, 10f, 24f, 25f, 40f })
        {
            var evaluated = output.Prepare(&voice, false, 10, 0, new(0, 0, distance), enemy: EnemySoundKind.Boss);
            check(distance >= 25 ? evaluated.Route == HapticRoute.DistanceCutoff : evaluated.Spatial.Valid &&
                evaluated.Spatial.Distance == distance && evaluated.Spatial.Left > 0,
                "Enemy feedback ignored the recommended character-based 25-yalm cutoff");
            if (distance <= 3) check(evaluated.Spatial.Left == 1, "Nearby enemy feedback was attenuated");
        }
        check(output.Prepare(&voice, false, 2, 0, enemy: EnemySoundKind.Boss).Route == HapticRoute.MissingPosition,
            "Unpositioned enemies fell back to centered vibration");
        output.SetListener(null);
        check(output.Prepare(&voice, false, 2, 0, new(2, 0, 0), enemy: EnemySoundKind.Boss).Route == HapticRoute.MissingListener,
            "Enemies without a live listener fell back to non-spatial vibration");
        output.SetListener(new(Vector3.Zero, -Vector3.UnitX, Stopwatch.GetTimestamp()));
        var turned = output.Prepare(&voice, false, 2, 0, new(2, 0, 0), enemy: EnemySoundKind.Boss);
        check(turned.Spatial.Left == 1 && turned.Spatial.Right == 0, "Enemy direction failed to follow camera facing");
        output.Mix(turned, pcm, 2048, 0);
        check(calls == 2 && lastLeft == 0.1f && lastRight == 0 && output.ImpactCue == null,
            "Enemy output polluted the player's impact guidance");
        sends[0].Destination = (nint)destination;
        check(output.Prepare(&voice, false, 2, 0, new(2, 0, 0), enemy: EnemySoundKind.Boss).Route == HapticRoute.Native,
            "Existing enemy vibration was mixed a second time");
        sends[0].Destination = 123;
        check(new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice)).SequenceEqual(originalVoice) && pcm[0] == 0.1f && pcm[1] == 0,
            "Added enemy feedback changed native audio or speaker sends");
        foreach (var reason in new[] { "background", "attribution", "routing", "pvp/logout" })
        {
            output.SetEnemyContext(true); output.Configure(requested, true, true);
            frame = output.Prepare(&voice, false, 2, 0, new(2, 0, 0), enemy: EnemySoundKind.Boss);
            if (reason == "pvp/logout") output.SetEnemyContext(false);
            else output.Configure(requested, reason != "background", reason != "attribution", reason != "routing");
            output.Mix(frame, pcm, 2048, 0);
            check(!output.EnemiesActive && calls == 2, "Queued enemy output survived " + reason);
        }
        output.SetEnemyContext(true);
        var regularOnly = requested with { BossEnabled = false, RegularEnemyEnabled = true, EnemyStrength = 0.4f };
        output.Configure(regularOnly, true, true);
        check(output.Prepare(&voice, false, 2, 0, new(2, 0, 0), enemy: EnemySoundKind.Boss).Destination == 0,
            "Regular-enemy toggle enabled bosses");
        frame = output.Prepare(&voice, false, 2, 0, new(-2, 0, 0), enemy: EnemySoundKind.Regular);
        output.Mix(frame, pcm, 2048, 0);
        check(calls == 3 && lastGain == 0.4f && output.RegularEnemyBlocks == 1, "Regular-enemy feedback did not use its enabled channel");
        output.Configure(regularOnly with { EnemyStrength = 0 }, true, true);
        output.Mix(frame, pcm, 2048, 0);
        check(calls == 3 && !output.Active, "Zero strength or stale settings retained enemy output");
        var hits = new HapticSettings(HitReactions: true, EnemyStrength: 0.4f, Spatial: true);
        output.Configure(hits, true, true);
        frame = output.Prepare(&voice, true, 2, 0, Vector3.Zero, hitReaction: true);
        output.Mix(frame, pcm, 2048, 0);
        check(calls == 4 && frame.HitReaction && frame.Spatial.Valid && lastGain == 0.4f &&
            output.HitReactionBlocks == 1 && !output.CurrentSettings.Enabled && !output.CurrentSettings.EnemiesEnabled,
            "Hit-reaction-only mode required player/monster skill feedback or lost its independent routing");
        output.Configure(new HapticSettings(Enabled: true, Spatial: true), true, true);
        check(output.Prepare(&voice, true, 2, 0, Vector3.Zero, hitReaction: true).Destination == 0 &&
            output.Prepare(&voice, true, 2, 0, Vector3.Zero).Destination != 0,
            "Disabling hit reactions fell back to Player SFX or muted the player's skills");
    }

    private static void CheckVoiceOwnershipSnapshot(Action<bool, string> check)
    {
        var allocation = NativeMemory.AllocZeroed((nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        var pool = (GameAudioLayout.SoundData*)allocation;
        var cueReads = 0;
        try
        {
            var ownership = new GameAudioLayout.PlayerSoundOwnership(pool, _ => { cueReads++; return false; });
            ownership.SetPlayer(100, 1000, 10000);
            var player = ownership.Player;
            var bossActor = new GameAudioLayout.EnemyActor(200, 2000, 20000, 20, EnemySoundKind.Boss);
            ownership.SetEnemies(new[] { bossActor }, 1);
            var boss = ownership.ResolveCaster(200, 0);
            pool->IsActive = pool->IsPositional = 1;
            pool->ResourceHandle = 999;
            pool->SoundId = 101; pool->Position = new(1, 2, 3);
            ownership.Acquired(pool, player, action: new(EnemyActionKind.HitReaction, 7));
            var hit = ownership.Inspect(101, true, true);
            check(hit is { Owned: true, HitReaction: true, Enemy: EnemySoundKind.None } && hit.Position == new Vector3(1, 2, 3) && cueReads == 0,
                "One voice observation did not retain its player hit identity and position without enemy classification");

            // Reuse occurs at the existing boundary between Render's former
            // Contains/IsPlayerHitReaction/TryGetPosition calls. The captured
            // voice must keep its original hit policy and copied position.
            ownership.Acquired(pool, null);
            pool->SoundId = 202; pool->Position = new(4, 5, 6);
            ownership.Acquired(pool, player);
            var settings = new HapticSettings(Enabled: true, Strength: 0.8f, HitReactions: false);
            check(hit.HitReaction && hit.Position == new Vector3(1, 2, 3) &&
                settings.AdditionalGain(hit.Owned, 2, 0, false, hit.Enemy, hit.HitReaction) == 0,
                "Same-player slot reuse changed an already observed hit into enabled player-skill feedback");
            check(ownership.Inspect(101, true, true) == default && ownership.Inspect(202, true, true) is
                { Owned: true, HitReaction: false, Enemy: EnemySoundKind.None, Position: { X: 4, Y: 5, Z: 6 } },
                "Same-player slot reuse revived the old playback or retained its hit classification");
            check(ownership.Inspect(202, false, false) is { Owned: true, Position: null } && cueReads == 0,
                "Lighting-only inspection required positional or enemy filename work");

            // Positional handles can be reused verbatim. The new lifetime must
            // establish its own positive playback link before it can react.
            pool->SoundId = -11;
            ownership.Acquired(pool, player);
            ownership.LinkPlayback(-11, 9001);
            var linked = ownership.Inspect(9001, false, true);
            ownership.Acquired(pool, player);
            check(linked.Owned && linked.Position == new Vector3(4, 5, 6) &&
                ownership.Inspect(9001, true, true) == default,
                "Identical native handle reuse inherited a preceding generation's playback link");
            ownership.LinkPlayback(-11, 9002);
            check(ownership.Inspect(9002, false, true).Owned && ownership.Inspect(9001, true, true) == default,
                "A replacement positional playback failed to establish its independent mapping");

            var released = new EnemyActionTag(EnemyActionKind.Skill, 7, Phase: EnemyActionPhase.ReleasedEffect);
            pool->SoundId = 303; ownership.Acquired(pool, boss, action: released);
            check(ownership.Inspect(303, false, false) == default && cueReads == 0,
                "A player-only voice inspection processed an enemy resource");
            var enemy = ownership.Inspect(303, true, true);
            check(enemy is { Owned: false, HitReaction: false, Enemy: EnemySoundKind.Boss } &&
                enemy.Position == new Vector3(4, 5, 6) && cueReads == 1,
                "A validated enemy voice lost its category, copied position or targeting-cue guard");
            ownership.SetEnemies(default, 1);
            check(ownership.Inspect(303, true, true) == default,
                "Despawning the actor left its queued sound valid in voice inspection");
            ownership.SetEnemies(new[] { bossActor }, 1);
            pool->SoundId = 404; ownership.Acquired(pool, player);
            pool->IsPositional = 0;
            check(ownership.Inspect(404, true, true) is { Owned: true, Position: null },
                "A non-positional voice invented a position");
            pool->IsPositional = 1; pool->Position.X = float.NaN;
            check(ownership.Inspect(404, true, true) is { Owned: true, Position: null },
                "A nonfinite position contaminated an otherwise valid ownership observation");
            pool->Position = new(7, 8, 9);
            for (var i = 0; i < 1000; i++) ownership.Inspect(404, true, true);
            var accepted = 0;
            Action inspect = () =>
            {
                if (ownership.Inspect(404, true, true) is { Owned: true, HitReaction: false, Position: { X: 7, Y: 8, Z: 9 } }) accepted++;
            };
            var allocated = AllocationChecks.MeasureAllocations(inspect, warmup: 0, iterations: 10000);
            check(accepted == 10000 && allocated == 0,
                $"Per-voice ownership inspection allocated {allocated} bytes or lost valid observations");
            ownership.SetPlayer(0, 0, 0);
            check(ownership.Inspect(404, true, true) == default && ownership.Inspect(0, true, true) == default,
                "Voice inspection survived logout or accepted a nonpositive playback ID");
        }
        finally { NativeMemory.Free(allocation); }
    }

    private static NativeHapticOutput.Frame PrepareCategory(NativeHapticOutput output,
        GameAudioLayout.SourceVoice* voice, int category, Vector3? position, int soundId = 500) => category switch
    {
        0 => output.Prepare(voice, true, 2, 0, position, soundId),
        1 => output.Prepare(voice, false, 2, 0, position, soundId, EnemySoundKind.Boss),
        2 => output.Prepare(voice, false, 10, 0, position, soundId, EnemySoundKind.Regular),
        3 => output.Prepare(voice, true, 2, 0, position, soundId, hitReaction: true),
        4 => output.Prepare(voice, false, 5, 0, position, soundId, EnemySoundKind.Boss, arenaEffect: true),
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    private static void CheckSharedSpatialSettings(Action<bool, string> check)
    {
        var table = stackalloc nint[16]; new Span<nint>(table, 16).Clear();
        var mix = (nint)(delegate* unmanaged<nint, float*, uint, float, uint, void>)&Capture; table[15] = mix;
        var destination = stackalloc byte[0x160]; new Span<byte>(destination, 0x160).Clear();
        *(nint*)destination = (nint)table; *(int*)(destination + 0xE0) = 1;
        "Vibration\0"u8.CopyTo(new Span<byte>(destination + 0x13E, 10));
        var graph = stackalloc nint[15]; new Span<nint>(graph, 15).Clear(); graph[9] = (nint)destination;
        var output = new NativeHapticOutput(graph, mix, (nint)table, (nint)table + 16 * 8);
        var bed = new float[16]; bed[0] = bed[8] = 1;
        var ambisonic = new float[32]; ambisonic[0] = ambisonic[16] = 1;
        output.SetSpatialMatrices(bed, ambisonic);
        output.SetEnemyContext(true);
        var shared = new HapticSettings(Enabled: true, Strength: 0.6f, Spatial: true, DistanceFalloff: true,
            NearDistance: 3, FarDistance: 25, AutomaticDistance: false, FollowImpact: true, ImpactBias: 1,
            BossEnabled: true, RegularEnemyEnabled: true, EnemyStrength: 0.6f, HitReactions: true);
        GameAudioLayout.SourceVoice voice = default; voice.SendCount = 1;
        var sends = (GameAudioLayout.VoiceSend*)((byte*)&voice + 0x940); sends[0].Destination = 123; sends[0].Gain = 0.75f;
        var originalVoice = new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice)).ToArray();
        var pcm = stackalloc float[2048]; new Span<float>(pcm, 2048).Clear();
        for (var i = 0; i < 256; i++) { pcm[i * 8] = 0.1f; pcm[i * 8 + 7] = 0.02f; }
        var originalPcm = new ReadOnlySpan<float>(pcm, 2048).ToArray();
        var categories = new[] { "player", "boss", "regular enemy", "hit reaction", "arena" };
        calls = 0;

        for (var mask = 0; mask < 4; mask++)
        {
            var settings = shared with { Spatial = (mask & 1) != 0, DistanceFalloff = (mask & 2) != 0 };
            output.Configure(settings, true, true);
            check(output.WantsSpatial == (mask != 0), "Listener demand ignored an independent spatial/fade toggle");
            for (var category = 0; category < categories.Length; category++)
            {
                // Both off must use the original route even without a position or listener.
                output.SetListener(mask == 0 ? null : new(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp(), new(0, 0, 10)));
                var frame = PrepareCategory(output, &voice, category, mask == 0 ? null : new Vector3(14, 0, 0));
                var fade = settings.DistanceFalloff ? 0.5f : 1;
                check(frame.HasOutput && frame.Gain == 0.6f && !frame.Biased &&
                    (mask == 0 ? frame.Route == HapticRoute.Original && !frame.Spatial.Valid :
                        frame.Route == HapticRoute.Spatial && frame.Spatial.Valid && frame.Spatial.Distance == 14 &&
                        frame.Spatial.Left == (settings.Spatial ? 0 : fade) && frame.Spatial.Right == fade),
                    "Shared independent spatial/fade settings were not applied to " + categories[category]);
                var before = calls;
                output.Mix(frame, pcm, 2048, 0);
                check(calls == before + 1 && lastGain == 0.6f && lastFrames == 256 && lastFormat == 0 &&
                    (mask == 0 ? lastSamples == (nint)pcm && lastLeft == 0.1f && lastRight == 0 :
                        lastLeft == 0.1f * frame.Spatial.Left && lastRight == 0.1f * frame.Spatial.Right),
                    "Shared spatial/fade settings changed the PCM route or strength for " + categories[category]);

                output.SetListener(null);
                var missingListener = PrepareCategory(output, &voice, category, new(2, 0, 0));
                var missingPosition = PrepareCategory(output, &voice, category, null);
                check(settings.DistanceFalloff ? missingListener.Route == HapticRoute.MissingListener &&
                    missingPosition.Route == HapticRoute.MissingPosition :
                    missingListener.Route == HapticRoute.Original && missingPosition.Route == HapticRoute.Original,
                    "The selected fade policy was bypassed by missing source/listener data for " + categories[category]);
            }
        }

        // Camera-relative custom limits must not be replaced by the former fixed enemy defaults.
        // At character distance 25 this source is only 5 from the camera: halfway through the custom 2–8 fade.
        foreach (var spatial in new[] { false, true })
        {
            var camera = shared with { Spatial = spatial, DistanceOrigin = HapticDistanceOrigin.Camera, NearDistance = 2, FarDistance = 8 };
            output.Configure(camera, true, true);
            output.SetListener(new(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp(), new(20, 0, 0)));
            for (var category = 0; category < categories.Length; category++)
            {
                var within = PrepareCategory(output, &voice, category, new(25, 0, 0));
                check(within.Route == HapticRoute.Spatial && within.Spatial.Distance == 5 &&
                    within.Spatial.Left == (spatial ? 0 : 0.5f) && within.Spatial.Right == 0.5f,
                    "Camera/custom distance settings were overridden for " + categories[category]);
                check(PrepareCategory(output, &voice, category, new(28, 0, 0)).Route == HapticRoute.DistanceCutoff,
                    "The custom camera cutoff was ignored for " + categories[category]);
            }

            // The recommended camera envelope follows zoom: the 20-yalm offset moves 3–25 to 23–45.
            output.Configure(shared with { Spatial = spatial, DistanceOrigin = HapticDistanceOrigin.Camera, AutomaticDistance = true }, true, true);
            output.SetListener(new(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp(), new(20, 0, 0)));
            for (var category = 0; category < categories.Length; category++)
            {
                var middle = PrepareCategory(output, &voice, category, new(54, 0, 0));
                check(middle.Route == HapticRoute.Spatial && middle.Spatial.Distance == 34 &&
                    middle.Spatial.Left == (spatial ? 0 : 0.5f) && middle.Spatial.Right == 0.5f &&
                    PrepareCategory(output, &voice, category, new(65, 0, 0)).Route == HapticRoute.DistanceCutoff,
                    "Recommended camera distance failed to follow zoom for " + categories[category]);
            }

            var recommended = shared with { Spatial = spatial, AutomaticDistance = true };
            output.Configure(recommended, true, true);
            output.SetListener(new(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp(), new(0, 0, 60)));
            for (var category = 0; category < categories.Length; category++)
            {
                var near = PrepareCategory(output, &voice, category, new(0, 0, 3));
                check(near.Spatial is { Valid: true, Left: 1, Right: 1 } &&
                    PrepareCategory(output, &voice, category, new(0, 0, 24)).HasOutput &&
                    PrepareCategory(output, &voice, category, new(0, 0, 25)).Route == HapticRoute.DistanceCutoff,
                    "Recommended character distance did not preserve near feedback and cut off at 25 for " + categories[category]);
            }
        }

        // Impact guidance is learned from the player's skills; it must never steer enemy/arena/hit audio.
        var directional = shared with { DistanceFalloff = false };
        output.Configure(directional, true, true);
        output.SetListener(new(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp()));
        output.Mix(PrepareCategory(output, &voice, 0, new(2, 0, 0), 901), pcm, 2048, 0);
        output.Configure(directional, true, true); // Publish the observed player impact without changing the settings epoch.
        var guided = PrepareCategory(output, &voice, 0, Vector3.Zero);
        check(guided.Biased && guided.Spatial.Left == 0 && guided.Spatial.Right == 1 && output.ImpactCue?.SoundId == 901,
            "The shared spatial path lost the player's impact guidance");
        for (var category = 1; category < categories.Length; category++)
        {
            var independent = PrepareCategory(output, &voice, category, Vector3.Zero, 902 + category);
            check(!independent.Biased && independent.Spatial is { Valid: true, Pan: 0, Left: 1, Right: 1 },
                "Player impact guidance steered " + categories[category]);
            output.Mix(independent, pcm, 2048, 0);
        }
        output.Configure(directional, true, true);
        check(output.ImpactCue?.SoundId == 901, "Enemy, arena or hit audio replaced the player's impact guide");

        var changes = new[]
        {
            shared with { Spatial = false }, shared with { DistanceFalloff = false },
            shared with { DistanceOrigin = HapticDistanceOrigin.Camera }, shared with { NearDistance = 4 },
            shared with { FarDistance = 30 }, shared with { AutomaticDistance = true },
            shared with { FollowImpact = false }, shared with { ImpactBias = 0.25f },
        };
        foreach (var changed in changes)
            for (var category = 0; category < categories.Length; category++)
            {
                output.Configure(shared, true, true);
                output.SetListener(new(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp()));
                var queued = PrepareCategory(output, &voice, category, new(2, 0, 0));
                check(queued.HasOutput, "Stale-setting regression did not prepare a real " + categories[category] + " frame");
                var before = calls;
                output.Configure(changed, true, true);
                output.Mix(queued, pcm, 2048, 0);
                output.Configure(shared, true, true);
                output.Mix(queued, pcm, 2048, 0);
                check(calls == before, "A queued " + categories[category] + " frame survived changing/restoring shared spatial settings");
            }

        // Native vibration sends take precedence under every setting combination and category.
        sends[0].Destination = (nint)destination;
        for (var mask = 0; mask < 4; mask++)
        {
            output.Configure(shared with { Spatial = (mask & 1) != 0, DistanceFalloff = (mask & 2) != 0 }, true, true);
            output.SetListener(null);
            for (var category = 0; category < categories.Length; category++)
            {
                var native = PrepareCategory(output, &voice, category, null);
                var before = calls;
                output.Mix(native, pcm, 2048, 0);
                check(native.Route == HapticRoute.Native && !native.HasOutput && calls == before,
                    "Shared spatial settings duplicated an existing native vibration send for " + categories[category]);
            }
        }
        sends[0].Destination = 123;
        check(new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice)).SequenceEqual(originalVoice) &&
            new ReadOnlySpan<float>(pcm, 2048).SequenceEqual(originalPcm),
            "Shared combat routing modified source PCM, speaker sends or native voice state");
    }
}
