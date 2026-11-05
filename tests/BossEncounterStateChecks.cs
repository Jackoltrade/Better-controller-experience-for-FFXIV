// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using DualSenseLighting.Core;

internal static class BossEncounterStateChecks
{
    private static GameAudioLayout.EnemyActor Regular(int index) => new(1000 + index,
        (uint)(2000 + index), 3000 + index, index, EnemySoundKind.Regular, NameId: (uint)(4000 + index), BaseId: (uint)(5000 + index));

    internal static void Run(Action<bool, string> check)
    {
        CheckEnrollment(check);
        CheckIdentity(check);
        CheckContextLifetime(check);
        CheckBounds(check);
        CheckClassification(check);
        CheckRosterPipeline(check);
        CheckPlayerOwnedClosure(check);
        CheckOwnershipCyclesAndInvalids(check);
        CheckAllocation(check);
        CheckOwnershipAllocation(check);
    }

    private static void CheckRosterPipeline(Action<bool, string> check)
    {
        var state = new BossEncounterState(1);
        var known = Regular(1) with { Role = EnemyActorRole.Combatant };
        var fresh = Regular(2) with { Role = EnemyActorRole.Combatant };
        var owned = Regular(3) with { Role = EnemyActorRole.Combatant, OwnerId = 12345 };
        var candidates = new[] { known, owned };
        var selected = new GameAudioLayout.EnemyActor[3];
        var kept = state.ExcludePlayerOwned(candidates, new uint[] { 12345 });
        state.Update(100, 263, 12345, false, false);
        check(state.SelectRoster(candidates.AsSpan(0, kept), selected) == 1 && selected[0] == known && state.Count == 1,
            "Roster pipeline enrolled player-owned actors or lost independently regular enemies");
        candidates = new[] { known, fresh };
        state.Update(100, 263, 12345, true, true);
        check(state.SelectRoster(candidates, selected) == 2 && selected[0].Kind == EnemySoundKind.Regular &&
            selected[1].Kind == EnemySoundKind.Boss && selected[1].Role == EnemyActorRole.BossEncounter && state.Count == 1,
            "Active roster pipeline changed regular memory or failed to classify new boss performers");
        state.Update(100, 263, 12345, false, false);
        candidates = new[] { known, fresh };
        state.SelectRoster(candidates, selected);
        state.Update(100, 263, 12345, true, true);
        candidates = new[] { known, fresh };
        check(state.Saturated && state.SelectRoster(candidates, selected) == 2 && selected[1].Kind == EnemySoundKind.Regular,
            "Saturated roster pipeline enabled uncertain boss remapping");
        state.Update(101, 263, 12345, true, true);
        candidates = new[] { known };
        check(state.SelectRoster(candidates, selected) == 1 && selected[0].Kind == EnemySoundKind.Boss && state.Count == 0 &&
            state.SelectRoster(Span<GameAudioLayout.EnemyActor>.Empty, selected) == 0,
            "Roster pipeline retained old-scene regular memory or failed on an empty roster");
    }

    private static void CheckEnrollment(Action<bool, string> check)
    {
        var state = new BossEncounterState(); var known = Regular(1); var newcomer = Regular(2);
        check(!state.Active && !state.Saturated && state.Count == 0 && !state.RememberRegular(known),
            "Regular NPC memory enrolled an entity before a valid scene/player context existed");
        state.Update(100, 263, 12345, false, false);
        check(state.RememberRegular(known) && state.WasRegular(known) && state.Count == 1,
            "An independently known regular NPC could not be remembered before boss combat");
        state.Update(100, 263, 12345, true, true);
        check(state.Active && state.WasRegular(known) && !state.WasRegular(newcomer) &&
            !state.RememberRegular(newcomer) && state.Count == 1,
            "Boss combat forgot pre-existing regulars or enrolled a newly spawned mechanic as pre-existing trash");
        state.Update(100, 263, 12345, false, true);
        check(!state.Active && !state.RememberRegular(newcomer) && !state.RememberRegular(known) && state.Count == 1,
            "Joining late or leaving local combat reopened enrollment while the boss was still physically in combat");
        state.Update(100, 263, 12345, true, false);
        check(!state.Active && state.RememberRegular(newcomer) && state.WasRegular(known) && state.Count == 2,
            "Ordinary player combat with no active boss incorrectly blocked regular NPC enrollment");
        state.Update(100, 263, 12345, false, false);
        for (var i = 0; i < 100; i++) state.Update(100, 263, 12345, false, false);
        state.Update(100, 263, 12345, true, true);
        check(state.Active && state.WasRegular(known) && state.WasRegular(newcomer) && state.Count == 2,
            "Combat exit, idle updates, or re-entering combat expired remembered regular NPCs");
        // Party size is deliberately absent from this state API. Different
        // valid duties use the same physical player/boss combat rule.
        foreach (var duty in new uint[] { 1, 263, 537, 4000 })
        {
            var encounter = new BossEncounterState(1); encounter.Update(100, duty, 12345, true, true);
            check(encounter.Active, "Boss encounter state required a particular duty or implicit party-size allowlist");
        }
    }

    private static void CheckIdentity(Action<bool, string> check)
    {
        var state = new BossEncounterState(32); state.Update(100, 263, 12345, false, false);
        for (var i = 0; i < 16; i++)
        {
            var actor = Regular(i);
            check(state.RememberRegular(actor) && state.WasRegular(actor), "Varied regular NPC identities were not remembered");
            check(!state.WasRegular(actor with { Actor = actor.Actor + 10000 }) &&
                !state.WasRegular(actor with { EntityId = actor.EntityId + 10000 }) &&
                !state.WasRegular(actor with { BaseId = actor.BaseId + 10000 }),
                "A different address, reused entity slot, or different NPC base inherited a regular NPC exemption");
            var reclassified = actor with { Kind = EnemySoundKind.Boss, Role = EnemyActorRole.BossEncounter,
                NameId = 99999, ObjectIndex = 999, DrawObject = 99999, OwnerId = 0xE0000000,
                BossOwnerNameId = 88888, EventId = 0x80030064, LayoutId = 4444 };
            check(state.WasRegular(reclassified),
                "A mutable display, draw, role, or encounter field erased an exact previously known regular identity");
        }
        var noBase = Regular(20) with { BaseId = 0 };
        check(state.RememberRegular(noBase) && state.WasRegular(noBase) && !state.WasRegular(noBase with { BaseId = 1 }),
            "A zero base entry was rejected despite a valid address/entity identity, or was treated as a wildcard");
        var count = state.Count;
        foreach (var invalid in new[] { Regular(1) with { Actor = 0 }, Regular(1) with { Actor = -1 },
            Regular(1) with { EntityId = 0 }, Regular(1) with { EntityId = 0xE0000000 } })
            check(!state.RememberRegular(invalid) && !state.WasRegular(invalid) && state.Count == count,
                "An invalid native actor identity entered or matched regular NPC memory");
        check(state.RememberRegular(Regular(1)) && state.Count == count,
            "Repeated observation of the same regular NPC consumed additional bounded capacity");
    }

    private static void CheckContextLifetime(Action<bool, string> check)
    {
        var actor = Regular(1);
        foreach (var change in new[] { "territory", "duty", "player", "no territory", "no duty", "no player", "invalid player" })
        {
            var state = new BossEncounterState(1); state.Update(100, 263, 12345, false, false);
            state.RememberRegular(actor); state.RememberRegular(Regular(2));
            check(state.Saturated, "Context-reset fixture did not reach bounded saturation");
            switch (change)
            {
                case "territory": state.Update(101, 263, 12345, true, true); break;
                case "duty": state.Update(100, 264, 12345, true, true); break;
                case "player": state.Update(100, 263, 12346, true, true); break;
                case "no territory": state.Update(0, 263, 12345, true, true); break;
                case "no duty": state.Update(100, 0, 12345, true, true); break;
                case "no player": state.Update(100, 263, 0, true, true); break;
                default: state.Update(100, 263, 0xE0000000, true, true); break;
            }
            check(state.Count == 0 && !state.Saturated && !state.WasRegular(actor),
                "An earlier scene/player's regular memory or saturation survived " + change);
            var valid = change is "territory" or "duty" or "player";
            check(state.Active == valid, "A changed or invalid context retained the wrong boss-combat state after " + change);
            if (!valid)
                check(!state.RememberRegular(actor), "An invalid scene/player context reopened enrollment after " + change);
            state.Update(100, 263, 12345, false, false);
            check(state.RememberRegular(actor) && state.Count == 1 && !state.Saturated,
                "A valid scene/player context did not recover regular enrollment after " + change);
        }
    }

    private static void CheckBounds(Action<bool, string> check)
    {
        foreach (var capacity in new[] { -1, 0, 4097, int.MaxValue })
        {
            var rejected = false;
            try { _ = new BossEncounterState(capacity); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "An unsupported regular-NPC memory capacity was accepted");
        }
        var state = new BossEncounterState(2); state.Update(100, 263, 12345, false, false);
        check(state.RememberRegular(Regular(1)) && state.RememberRegular(Regular(2)) && !state.Saturated && state.Count == 2,
            "Regular-NPC memory saturated before reaching its configured capacity");
        check(!state.RememberRegular(Regular(3)) && state.Saturated && state.Count == 2 &&
            state.WasRegular(Regular(1)) && state.WasRegular(Regular(2)) && !state.WasRegular(Regular(3)),
            "Memory overflow evicted a regular NPC or silently exempted an unrecorded entity");
        state.Update(100, 263, 12345, true, true);
        check(!state.Active && !state.RememberRegular(Regular(1)) && state.Saturated && state.Count == 2,
            "Saturation enabled broad boss routing or bypassed the physical-boss enrollment guard");
        state.Update(100, 263, 12345, false, false);
        state.Update(100, 263, 12345, true, true);
        check(!state.Active && state.Saturated && state.Count == 2,
            "Combat/focus-style state transitions cleared saturation without a scene/player identity reset");
        var maximum = new BossEncounterState(); maximum.Update(100, 263, 12345, false, false);
        var allAdded = true;
        for (var i = 0; i < 4096; i++) allAdded &= maximum.RememberRegular(Regular(i));
        check(allAdded && maximum.Count == 4096 && !maximum.Saturated &&
            !maximum.RememberRegular(Regular(4096)) && maximum.Saturated && maximum.Count == 4096,
            "Default regular-NPC memory did not enforce its fixed maximum without evicting known entities");
    }

    private static void CheckClassification(Action<bool, string> check)
    {
        var state = new BossEncounterState(4);
        var neutral = Regular(100) with { Kind = EnemySoundKind.None, Role = EnemyActorRole.Unclassified,
            OwnerId = 0xE0000000, BossOwnerNameId = 99999, EventId = 0x80030064, LayoutId = 123456 };
        check(state.Classify(neutral) == neutral, "An NPC was promoted without a valid encounter context");
        var known = Regular(1);
        state.Update(100, 263, 12345, false, false); state.RememberRegular(known);
        check(state.Classify(neutral) == neutral, "An inactive instanced encounter promoted a neutral NPC");
        state.Update(100, 263, 12345, true, false);
        check(state.Classify(neutral) == neutral, "Player combat alone promoted NPC audio to the boss category");
        state.Update(100, 263, 12345, false, true);
        check(state.Classify(neutral) == neutral, "Boss combat alone promoted NPC audio before the player joined combat");
        state.Update(100, 263, 12345, true, true);
        for (var i = 0; i < 16; i++)
        {
            var actor = neutral with { Actor = neutral.Actor + i, EntityId = neutral.EntityId + (uint)i,
                DrawObject = neutral.DrawObject + i, ObjectIndex = i, NameId = neutral.NameId + (uint)i,
                BaseId = i == 0 ? 0u : neutral.BaseId + (uint)i, OwnerId = i % 2 == 0 ? 0u : 0xE0000000,
                LayoutId = neutral.LayoutId + (uint)i };
            check(state.Classify(actor) == actor with { Kind = EnemySoundKind.Boss, Role = EnemyActorRole.BossEncounter },
                "Active encounter classification required a name/base allowlist or changed native identity/provenance metadata");
        }
        var changedPresentation = known with { Kind = EnemySoundKind.Boss, Role = EnemyActorRole.BossEncounter,
            NameId = 99999, DrawObject = 55555, ObjectIndex = 42, OwnerId = 9000, EventId = 0x80030064, LayoutId = 98765 };
        var regular = changedPresentation with { Kind = EnemySoundKind.Regular, Role = EnemyActorRole.Combatant };
        check(state.Classify(changedPresentation) == regular && state.Classify(state.Classify(changedPresentation)) == regular &&
            state.WasRegular(changedPresentation) && state.Count == 1,
            "A pre-existing regular NPC lost its category or metadata while the boss encounter was active");
        check(state.Classify(known with { BaseId = known.BaseId + 1 }) ==
            known with { BaseId = known.BaseId + 1, Kind = EnemySoundKind.Boss, Role = EnemyActorRole.BossEncounter },
            "A different native identity inherited the pre-existing regular NPC's classification exemption");
        foreach (var invalid in new[] { neutral with { Actor = 0 }, neutral with { Actor = -1 },
            neutral with { EntityId = 0 }, neutral with { EntityId = 0xE0000000 } })
            check(state.Classify(invalid) == invalid, "An invalid native actor was promoted during boss combat");
        var promoted = neutral with { Kind = EnemySoundKind.Boss, Role = EnemyActorRole.BossEncounter };
        for (var i = 0; i < 2000; i++) { state.Classify(neutral); state.Classify(changedPresentation); }
        var before = GC.GetAllocatedBytesForCurrentThread(); var matched = 0;
        for (var i = 0; i < 10000; i++)
            if (state.Classify(neutral) == promoted && state.Classify(changedPresentation) == regular) matched++;
        check(matched == 10000 && GC.GetAllocatedBytesForCurrentThread() == before,
            "Repeated active encounter classification allocated or lost regular-NPC exemptions");
        state.Update(100, 263, 12345, false, false);
        check(state.Classify(changedPresentation) == changedPresentation && state.Count == 1 && state.WasRegular(known),
            "Inactive classification rewrote input metadata or erased regular history");
        var saturated = new BossEncounterState(1); saturated.Update(100, 263, 12345, false, false);
        saturated.RememberRegular(known); saturated.RememberRegular(Regular(2));
        saturated.Update(100, 263, 12345, true, true);
        check(saturated.Saturated && !saturated.Active && saturated.Classify(neutral) == neutral,
            "Saturated regular memory still promoted an unmarked NPC into broad boss feedback");
    }

    private static void CheckAllocation(Action<bool, string> check)
    {
        var state = new BossEncounterState(4); var actor = Regular(1);
        state.Update(100, 263, 12345, false, false); state.RememberRegular(actor);
        for (var i = 0; i < 2000; i++) { state.RememberRegular(actor); state.WasRegular(actor); }
        var before = GC.GetAllocatedBytesForCurrentThread(); var found = 0;
        for (var i = 0; i < 10000; i++)
        {
            state.Update(100, 263, 12345, false, false);
            if (state.RememberRegular(actor) && state.WasRegular(actor)) found++;
        }
        check(found == 10000 && state.Count == 1 && GC.GetAllocatedBytesForCurrentThread() == before,
            "Stable framework updates or repeated regular identity lookups allocated or duplicated memory entries");
    }

    private static (GameAudioLayout.EnemyActor[] Actors, GameAudioLayout.EnemyActor[] Retained, uint[] Seeds) OwnershipFixture()
    {
        var direct = Regular(1) with { OwnerId = 111 };
        var grandchild = Regular(2) with { OwnerId = direct.EntityId };
        var pet = Regular(3) with { EntityId = 222, OwnerId = 0xE0000000 };
        var petChild = Regular(4) with { OwnerId = pet.EntityId };
        var buddy = Regular(5) with { EntityId = 333, OwnerId = 0 };
        var buddyChild = Regular(6) with { OwnerId = buddy.EntityId };
        var playerNpc = Regular(7) with { EntityId = 444, OwnerId = 77777 };
        var playerNpcChild = Regular(8) with { OwnerId = playerNpc.EntityId };
        var boss = Regular(9) with { Kind = EnemySoundKind.Boss, EntityId = 9000, OwnerId = 0xE0000000 };
        var bossAdd = Regular(10) with { OwnerId = boss.EntityId };
        var bossChild = Regular(11) with { OwnerId = bossAdd.EntityId };
        var unknownOwner = Regular(12) with { OwnerId = 77777 };
        var unownedZero = Regular(13) with { OwnerId = 0 };
        var unownedSentinel = Regular(14) with { OwnerId = 0xE0000000 };
        var seedIdentity = Regular(15) with { EntityId = 111, OwnerId = boss.EntityId };
        return (new[] { grandchild, petChild, buddyChild, playerNpcChild, bossChild, unknownOwner, bossAdd,
                unownedZero, playerNpc, buddy, pet, seedIdentity, boss, direct, unownedSentinel },
            new[] { bossChild, unknownOwner, bossAdd, unownedZero, boss, unownedSentinel },
            new uint[] { 111, 222, 333, 444, 0, 0xE0000000, 111 });
    }

    private static void CheckPlayerOwnedClosure(Action<bool, string> check)
    {
        var state = new BossEncounterState(1); var fixture = OwnershipFixture();
        state.Update(100, 263, 12345, false, false); state.RememberRegular(fixture.Retained[0]);
        state.Update(100, 263, 12345, true, true);
        var actors = (GameAudioLayout.EnemyActor[])fixture.Actors.Clone();
        var count = state.ExcludePlayerOwned(actors, fixture.Seeds);
        check(count == fixture.Retained.Length && actors.AsSpan(0, count).SequenceEqual(fixture.Retained),
            "Ownership closure retained a player/pet/buddy chain, removed an unowned/boss-owned NPC, or reordered survivors");
        check(state.Active && !state.Saturated && state.Count == 1 && state.WasRegular(fixture.Retained[0]) &&
            !state.WasRegular(fixture.Retained[1]),
            "Filtering player ownership modified the independent regular-NPC session memory");
        fixture.Actors.AsSpan().CopyTo(actors);
        check(state.ExcludePlayerOwned(actors, ReadOnlySpan<uint>.Empty) == fixture.Actors.Length &&
            actors.AsSpan().SequenceEqual(fixture.Actors),
            "Player ownership seeds leaked from a previous frame or a nonzero owner was assumed to be player-owned");
        fixture.Actors.AsSpan().CopyTo(actors);
        check(state.ExcludePlayerOwned(actors, new uint[] { 0, 0xE0000000 }) == fixture.Actors.Length,
            "Invalid player seeds classified unowned/default-owner NPCs as player-owned");
        state.Update(100, 263, 12345, false, false); state.RememberRegular(fixture.Retained[1]);
        fixture.Actors.AsSpan().CopyTo(actors);
        check(state.Saturated && state.ExcludePlayerOwned(actors, fixture.Seeds) == fixture.Retained.Length &&
            state.Saturated && state.Count == 1 && state.WasRegular(fixture.Retained[0]),
            "Player ownership filtering depended on saturation or cleared its separate fail-closed regular memory");

        var chain = new GameAudioLayout.EnemyActor[24];
        for (var i = 0; i < chain.Length; i++)
            chain[i] = Regular(100 + i) with { OwnerId = i == 0 ? 111u : (uint)(2000 + 100 + i - 1) };
        Array.Reverse(chain);
        check(state.ExcludePlayerOwned(chain, new uint[] { 111 }) == 0,
            "Player-owned nesting was truncated to a fixed depth or depended on ancestor-first actor order");
    }

    private static void CheckOwnershipCyclesAndInvalids(Action<bool, string> check)
    {
        var state = new BossEncounterState();
        var a = Regular(100) with { OwnerId = Regular(101).EntityId };
        var b = Regular(101) with { OwnerId = a.EntityId };
        var child = Regular(102) with { OwnerId = b.EntityId };
        var unowned = Regular(103) with { OwnerId = 0 };
        var source = new[] { child, b, unowned, a };
        var actors = (GameAudioLayout.EnemyActor[])source.Clone();
        check(state.ExcludePlayerOwned(actors, new uint[] { 111 }) == source.Length && actors.AsSpan().SequenceEqual(source),
            "A disconnected NPC ownership cycle was mistaken for player ownership");
        source.AsSpan().CopyTo(actors);
        check(state.ExcludePlayerOwned(actors, new uint[] { a.EntityId }) == 1 && actors[0] == unowned,
            "A positively identified player/pet seed inside an ownership cycle failed to exclude its descendants");
        var invalidParent = Regular(200) with { Actor = 0, OwnerId = 111 };
        var orphan = Regular(201) with { OwnerId = invalidParent.EntityId };
        var valid = Regular(202) with { OwnerId = 0xE0000000 };
        actors = new[] { invalidParent, Regular(203) with { Actor = -1 },
            Regular(204) with { EntityId = 0 }, Regular(205) with { EntityId = 0xE0000000 }, orphan, valid };
        check(state.ExcludePlayerOwned(actors, new uint[] { 111 }) == 2 && actors[0] == orphan && actors[1] == valid,
            "Invalid actors survived filtering or supplied a false player-ownership bridge to a valid orphan NPC");
        check(state.ExcludePlayerOwned(Span<GameAudioLayout.EnemyActor>.Empty, new uint[] { 111 }) == 0,
            "An empty native NPC roster produced ownership-filter output");
    }

    private static void CheckOwnershipAllocation(Action<bool, string> check)
    {
        var state = new BossEncounterState(); var fixture = OwnershipFixture();
        var actors = new GameAudioLayout.EnemyActor[fixture.Actors.Length];
        for (var i = 0; i < 2000; i++)
        { fixture.Actors.AsSpan().CopyTo(actors); state.ExcludePlayerOwned(actors, fixture.Seeds); }
        var before = GC.GetAllocatedBytesForCurrentThread(); var matched = 0;
        for (var i = 0; i < 10000; i++)
        {
            fixture.Actors.AsSpan().CopyTo(actors);
            if (state.ExcludePlayerOwned(actors, fixture.Seeds) == fixture.Retained.Length) matched++;
        }
        check(matched == 10000 && GC.GetAllocatedBytesForCurrentThread() == before &&
            actors.AsSpan(0, fixture.Retained.Length).SequenceEqual(fixture.Retained),
            "Repeated player-owned closure allocated or changed its stable compacted NPC output");
    }
}
