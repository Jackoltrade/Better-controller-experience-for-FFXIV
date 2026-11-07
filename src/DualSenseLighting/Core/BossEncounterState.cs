// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;

namespace DualSenseLighting.Core;

/// <summary>Framework-owned regular-enemy memory; audio receives only the resulting immutable roster.</summary>
public sealed class BossEncounterState
{
    public const int MaximumCapacity = 4096;
    private readonly int capacity;
    private readonly HashSet<Identity> regular;
    private readonly HashSet<uint> blocked = new();
    private uint territory, duty, player;
    private bool hasContext, bossCombat, playerCombat;
    private readonly record struct Identity(nint Actor, uint EntityId, uint BaseId);

    public BossEncounterState(int capacity = MaximumCapacity)
    {
        if (capacity is < 1 or > MaximumCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        regular = new HashSet<Identity>(capacity);
    }

    public bool Active => hasContext && playerCombat && bossCombat && !Saturated;
    public bool Saturated { get; private set; }
    public int Count => regular.Count;

    // Combat ending, focus loss and feature toggles do not change entity history.
    // A scene or player identity change starts a new lifetime for these marks.
    public void Update(uint territory, uint duty, uint playerEntityId, bool playerCombat, bool bossCombat)
    {
        if (territory == 0 || duty == 0 || playerEntityId is 0 or 0xE0000000)
        {
            regular.Clear(); Saturated = false; hasContext = false;
            this.territory = this.duty = player = 0;
            this.playerCombat = this.bossCombat = false;
            return;
        }
        if (!hasContext || this.territory != territory || this.duty != duty || player != playerEntityId)
        { regular.Clear(); Saturated = false; }
        this.territory = territory; this.duty = duty; player = playerEntityId;
        hasContext = true;
        this.playerCombat = playerCombat; this.bossCombat = bossCombat;
    }

    /// <summary>The caller supplies an independently classified regular enemy, before boss combat starts.</summary>
    public bool RememberRegular(GameAudioLayout.EnemyActor actor)
    {
        // Boss combat alone closes enrollment: arriving before the local player
        // joins must not mark a boss's new performers as pre-existing trash.
        if (!hasContext || bossCombat || !Valid(actor)) return false;
        var identity = new Identity(actor.Actor, actor.EntityId, actor.BaseId);
        if (regular.Contains(identity)) return true;
        if (regular.Count >= capacity)
        { Saturated = true; return false; }
        return regular.Add(identity);
    }

    public bool WasRegular(GameAudioLayout.EnemyActor actor) => hasContext && Valid(actor) &&
        regular.Contains(new Identity(actor.Actor, actor.EntityId, actor.BaseId));

    public GameAudioLayout.EnemyActor Classify(GameAudioLayout.EnemyActor actor) => !Active || !Valid(actor) ? actor :
        WasRegular(actor) ? actor with { Kind = EnemySoundKind.Regular, Role = EnemyActorRole.Combatant } :
        actor with { Kind = EnemySoundKind.Boss, Role = EnemyActorRole.BossEncounter };

    // Call after exclusions and Update: independently regular actors enroll
    // before active boss rules classify the remaining roster.
    public int SelectRoster(Span<GameAudioLayout.EnemyActor> candidates, Span<GameAudioLayout.EnemyActor> selected)
    {
        var count = GameAudioLayout.SelectEnemyActors(candidates, selected);
        for (var i = 0; i < count; i++)
            if (selected[i].Kind == EnemySoundKind.Regular) RememberRegular(selected[i]);
        if (Active)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                var actor = candidates[i];
                candidates[i] = FollowsRememberedRegular(actor, selected[..count])
                    ? actor with { Kind = EnemySoundKind.Regular, Role = EnemyActorRole.Combatant }
                    : Classify(actor);
            }
            count = GameAudioLayout.SelectEnemyActors(candidates, selected);
        }
        return count;
    }

    private bool FollowsRememberedRegular(GameAudioLayout.EnemyActor actor, ReadOnlySpan<GameAudioLayout.EnemyActor> selected)
    {
        if (actor.Role != EnemyActorRole.BossMechanic) return false;
        foreach (var helper in selected)
            if (helper.Actor == actor.Actor && helper.EntityId == actor.EntityId && helper.Kind == EnemySoundKind.Regular &&
                helper.Role == EnemyActorRole.BossMechanic)
                foreach (var root in selected)
                    if (root.Role == EnemyActorRole.Combatant && root.Kind == EnemySoundKind.Regular &&
                        root.NameId == actor.NameId && WasRegular(root)) return true;
        return false;
    }

    /// <summary>Compacts supported NPC candidates, excluding players and every proven player-owned descendant.</summary>
    public int ExcludePlayerOwned(Span<GameAudioLayout.EnemyActor> actors, ReadOnlySpan<uint> playerAndPetIds)
    {
        // Reject invalid actors before following their owner fields. Their
        // untrusted identity must not establish a chain for another NPC.
        var count = 0;
        foreach (var actor in actors)
            if (Valid(actor)) actors[count++] = actor;
        blocked.Clear();
        foreach (var id in playerAndPetIds)
            if (id is not (0 or 0xE0000000)) blocked.Add(id);
        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i < count; i++)
            {
                var actor = actors[i];
                if (BossMechanics.HasOwner(actor.OwnerId) && blocked.Contains(actor.OwnerId))
                    changed |= blocked.Add(actor.EntityId);
            }
        } while (changed);
        var kept = 0;
        for (var i = 0; i < count; i++)
        {
            var actor = actors[i];
            if (!blocked.Contains(actor.EntityId) &&
                (!BossMechanics.HasOwner(actor.OwnerId) || !blocked.Contains(actor.OwnerId)))
                actors[kept++] = actor;
        }
        return kept;
    }

    private static bool Valid(GameAudioLayout.EnemyActor actor) =>
        actor.Actor > 0 && actor.EntityId is not (0 or 0xE0000000);
}
