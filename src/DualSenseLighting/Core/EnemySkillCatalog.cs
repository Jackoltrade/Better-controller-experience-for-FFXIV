// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace DualSenseLighting.Core;

public enum EnemyActionKind { Unknown, AutoAttack, Skill, HitReaction }
public enum EnemyActionPhase { Unknown, Caster, ReleasedEffect, BossMechanic, Execution }
public enum EnemySoundBinding { Unknown, Default, Caster, Target, CasterOrigin }
public readonly record struct EnemyActionTag(EnemyActionKind Kind = EnemyActionKind.Unknown, uint ActionId = 0,
    uint TimelineId = 0, EnemyActionPhase Phase = EnemyActionPhase.Unknown, EnemySoundBinding Binding = EnemySoundBinding.Unknown,
    bool HasAttackType = false)
{
    public bool IsSkill => Kind == EnemyActionKind.Skill && (ActionId != 0 || TimelineId != 0 || Phase == EnemyActionPhase.BossMechanic);
    public bool IsReleasedEffect => IsSkill && Phase is EnemyActionPhase.ReleasedEffect or EnemyActionPhase.BossMechanic;
    public bool IsHitReaction => Kind == EnemyActionKind.HitReaction && (ActionId != 0 || TimelineId != 0);
}
public readonly record struct EnemyActionDefinition(uint Id, uint Category, uint End, uint Hit, uint Cast, uint AttackType = 0);
public readonly record struct EnemyTimelineDefinition(uint Id, string Key);

/// <summary>Immutable game-data classification. Shared auto-attack/skill timelines stay unknown.</summary>
public sealed class EnemySkillCatalog
{
    private readonly EnemyActionKind[] actions;
    private readonly Timeline[] timelines;
    private readonly Timeline[] modelTimelines;
    private readonly EnemyActionDefinition[] definitions;
    private readonly Dictionary<uint, string> timelineKeys;
    private sealed record Timeline(uint Id, string Key, byte Kinds, byte Phases, byte AttackTypes);

    public EnemySkillCatalog(IEnumerable<EnemyActionDefinition> definitions, IEnumerable<EnemyTimelineDefinition> timelineDefinitions)
    {
        var rows = definitions.ToArray();
        var maximum = rows.Length == 0 ? 0 : rows.Max(r => r.Id);
        if (maximum > 1_000_000) throw new NotSupportedException("Action IDs exceed the supported game-data range.");
        actions = new EnemyActionKind[maximum + 1];
        this.definitions = new EnemyActionDefinition[maximum + 1];
        var keys = timelineDefinitions.ToArray();
        timelineKeys = keys.ToDictionary(r => r.Id, r => r.Key);
        var kinds = new Dictionary<uint, byte>();
        var phases = new Dictionary<uint, byte>();
        var attackTypes = new Dictionary<uint, byte>();
        foreach (var row in rows)
        {
            var kind = Category(row);
            actions[row.Id] = kind;
            this.definitions[row.Id] = row;
            if (kind == EnemyActionKind.Unknown) continue;
            Add(row.End, kind, 4, row.AttackType); Add(row.Hit, kind, 2, row.AttackType); Add(row.Cast, kind, 1, row.AttackType);
        }
        void Add(uint id, EnemyActionKind kind, byte phase, uint attackType)
        {
            // -1/-2 are model-dependent auto-attack selectors, not Excel row IDs.
            if (id is 0 or > ushort.MaxValue) return;
            kinds.TryGetValue(id, out var before);
            kinds[id] = (byte)(before | (kind == EnemyActionKind.Skill ? 2 : 1));
            phases[id] = (byte)(phases.GetValueOrDefault(id) | phase);
            attackTypes[id] = (byte)(attackTypes.GetValueOrDefault(id) | AttackEvidence(attackType));
        }
        timelines = keys.Where(r => r.Id != 0 && !string.IsNullOrEmpty(r.Key))
            .GroupBy(r => r.Key, StringComparer.Ordinal).Select(group => new Timeline(group.Min(r => r.Id), group.Key,
                group.Aggregate((byte)0, (flags, r) => (byte)(flags | kinds.GetValueOrDefault(r.Id))),
                group.Aggregate((byte)0, (flags, r) => (byte)(flags | phases.GetValueOrDefault(r.Id))),
                group.Aggregate((byte)0, (flags, r) => (byte)(flags | attackTypes.GetValueOrDefault(r.Id)))))
            .OrderBy(r => r.Key, StringComparer.Ordinal).ToArray();
        modelTimelines = timelines.Where(r => r.Key.Contains("[SKL_ID]", StringComparison.Ordinal)).ToArray();
    }

    // Some NPC attacks have no category. Require defined attack metadata and
    // an execution/hit timeline before accepting an uncategorized action.
    private static EnemyActionKind Category(EnemyActionDefinition row) => row.Category == 1 ? EnemyActionKind.AutoAttack :
        row.Category is 2 or 3 or 4 or 9 or 13 or 15 or 17 ||
        row.Category == 0 && AttackEvidence(row.AttackType) == 2 &&
            (row.End is >= 1 and <= ushort.MaxValue || row.Hit is >= 1 and <= ushort.MaxValue)
            ? EnemyActionKind.Skill : EnemyActionKind.Unknown;

    // Installed AttackType rows 1..8 are defined; zero and 0xFFFFFFFF are used
    // by setup/unspecified actions. Mixed typed/setup evidence must stay unknown.
    private static byte AttackEvidence(uint attackType) => attackType is >= 1 and <= 8 ? (byte)2 : (byte)1;

    public EnemyActionTag Action(uint id, byte type)
    {
        var kind = type == 1 && id < actions.Length ? actions[id] : EnemyActionKind.Unknown;
        return new(kind, id, Phase: kind == EnemyActionKind.Skill ? EnemyActionPhase.ReleasedEffect : EnemyActionPhase.Unknown,
            HasAttackType: id < definitions.Length && AttackEvidence(definitions[id].AttackType) == 2);
    }

    public bool IsExecutionTimeline(uint actionId, uint timelineId) => actionId < definitions.Length && timelineId != 0 &&
        (definitions[actionId].End == timelineId || definitions[actionId].Hit == timelineId);

    public static bool CanUseAnimationFallback(EnemyActionTag timeline, EnemySoundBinding binding) =>
        timeline.IsSkill && timeline.Phase == EnemyActionPhase.Execution &&
        binding is EnemySoundBinding.Caster or EnemySoundBinding.CasterOrigin;

    // Live cast metadata supplies first-use evidence before the result callback.
    // Keep it on this exact identity; matching still uses the existing templates.
    public bool ObserveCast(GameAudioLayout.PlayerIdentity actor, uint actorEntityId, uint actionId, byte actionType)
    {
        if (actor.Actor == 0 || actor.EntityId is 0 or 0xE0000000 || actor.EntityId != actorEntityId ||
            !Action(actionId, actionType).IsSkill) return false;
        actor.ObserveAction(actionId);
        return true;
    }

    private static EnemyActionPhase Phase(byte phases) => phases == 1 ? EnemyActionPhase.Caster :
        phases == 2 ? EnemyActionPhase.ReleasedEffect : phases == 4 ? EnemyActionPhase.Execution : EnemyActionPhase.Unknown;

    public static EnemyActionTag TimelineSound(EnemyActionTag timeline, EnemySoundBinding binding)
    {
        var tag = timeline with { Binding = binding };
        // AnimationEnd is the skill's execution timeline, not necessarily casting.
        // Default/target tracks retain their existing rule. Positional caster
        // origin entries additionally need unambiguous attack-type metadata.
        // Cast-start, shared/unknown and basic-attack timelines cannot be promoted.
        return !tag.IsSkill || tag.Phase != EnemyActionPhase.Execution ? tag : tag with
        {
            Phase = binding is EnemySoundBinding.Default or EnemySoundBinding.Target ||
                binding == EnemySoundBinding.CasterOrigin && tag.HasAttackType
                    ? EnemyActionPhase.ReleasedEffect :
                binding is EnemySoundBinding.Caster or EnemySoundBinding.CasterOrigin ? EnemyActionPhase.Caster : EnemyActionPhase.Execution
        };
    }

    public static EnemyActionTag TimelineEffect(EnemyActionTag timeline) =>
        timeline.IsSkill && timeline.Phase == EnemyActionPhase.Execution
            ? timeline with { Phase = EnemyActionPhase.ReleasedEffect } :
        timeline.IsSkill && timeline.Phase == EnemyActionPhase.ReleasedEffect ? timeline : default;

    public EnemyActionTag ClassifySound(GameAudioLayout.EnemyActor source, GameAudioLayout.PlayerIdentity actor,
        byte origin, EnemyActionTag action, EnemyActionTag timeline)
    {
        var tag = action.Kind != EnemyActionKind.Unknown ? action : timeline;
        if (source.Kind != EnemySoundKind.Boss) return tag;
        // Never promote a confirmed basic attack. Character/voice tracks are body audio,
        // even if they happen to be created inside an action's result callback.
        if (tag.Kind == EnemyActionKind.AutoAttack) return tag;
        if (origin == 2) return tag with { Phase = EnemyActionPhase.Caster };
        if (source.Role is EnemyActorRole.BossMechanic or EnemyActorRole.BossSummon)
        {
            // Helpers require the same boss NPC name, a native owner link, or a
            // verified duty-specific mechanic association, plus a live boss anchor.
            // AVFX emitted by its non-combatant helper is released mechanic audio, not
            // the boss body's animation. Keep the action ID unknown when none was seen.
            // A verified encounter mechanic can attack before its delayed AVFX fires.
            // That history must not veto its explosion. Explicit auto-attack scopes
            // still return above; unverified/native-owned fighters keep the guard.
            var verifiedMechanic = source.Role == EnemyActorRole.BossSummon && source.BossOwnerNameId != 0;
            if (origin == 1 && (tag.IsSkill || tag.Kind == EnemyActionKind.Unknown && (verifiedMechanic || !HasAutoAttack(actor))) ||
                origin == 3 && tag.IsSkill)
                return tag with { Kind = EnemyActionKind.Skill, Phase = EnemyActionPhase.BossMechanic };
            return tag with { Phase = EnemyActionPhase.Unknown };
        }
        // A known caster track remains a caster track even inside the release callback.
        if (timeline.IsSkill && timeline.Phase != EnemyActionPhase.ReleasedEffect) return timeline;
        return origin is 1 or 3 ? tag : tag with { Phase = EnemyActionPhase.Unknown };
    }

    private bool HasAutoAttack(GameAudioLayout.PlayerIdentity actor)
    {
        for (var i = 0; i < GameAudioLayout.PlayerIdentity.ActionCapacity; i++)
        {
            var id = actor.ObservedAction(i);
            if (id < actions.Length && actions[id] == EnemyActionKind.AutoAttack) return true;
        }
        return false;
    }

    public EnemyActionTag PlayerTimelineKey(ReadOnlySpan<byte> key)
    {
        // Base hurt and defensive-reaction motions, including sheathed/drawn variants.
        // These are not the attacker's shared hit VFX or ordinary idle/attack motions.
        // Resolve their row IDs from this installation; do not hard-code an actor or sound bank.
        if (!key.SequenceEqual("normal/damage"u8) && !key.SequenceEqual("normal/damage_continuous"u8) &&
            !key.SequenceEqual("battle/damage"u8) && !key.SequenceEqual("battle/damage_continuous"u8) &&
            !key.SequenceEqual("battle/weapon_guard"u8) && !key.SequenceEqual("battle/shield_guard"u8)) return default;
        var timeline = TimelineKey(key);
        return timeline.TimelineId != 0 ? new(EnemyActionKind.HitReaction, TimelineId: timeline.TimelineId) : default;
    }

    public EnemyActionTag TimelineKey(ReadOnlySpan<byte> key, GameAudioLayout.PlayerIdentity? actor = null)
    {
        if (key.Length is 0 or > 255) return default;
        Span<char> characters = stackalloc char[key.Length];
        for (var i = 0; i < key.Length; i++)
        {
            if (key[i] is < 0x20 or > 0x7e) return default;
            characters[i] = (char)key[i];
        }
        byte observedKinds = 0, observedPhases = 0, observedAttackTypes = 0; uint observedTimeline = 0;
        Span<uint> ids = stackalloc uint[3];
        if (actor != null)
            for (var i = 0; i < GameAudioLayout.PlayerIdentity.ActionCapacity; i++)
            {
                var id = actor.ObservedAction(i);
                if (id == 0 || id >= definitions.Length) continue;
                var row = definitions[id];
                ids[0] = row.End; ids[1] = row.Hit; ids[2] = row.Cast;
                for (var part = 0; part < ids.Length; part++)
                {
                    var timeline = ids[part];
                    if (timeline != 0 && timelineKeys.TryGetValue(timeline, out var template) && Matches(template, characters))
                    {
                        observedKinds |= actions[id] == EnemyActionKind.Skill ? (byte)2 : actions[id] == EnemyActionKind.AutoAttack ? (byte)1 : (byte)0;
                        observedPhases |= part == 0 ? (byte)4 : part == 1 ? (byte)2 : (byte)1;
                        observedAttackTypes |= AttackEvidence(row.AttackType);
                        observedTimeline = timeline;
                    }
                }
            }
        if (observedKinds != 0) return new(observedKinds == 2 ? EnemyActionKind.Skill : observedKinds == 1
            ? EnemyActionKind.AutoAttack : EnemyActionKind.Unknown, TimelineId: observedTimeline, Phase: Phase(observedPhases),
                HasAttackType: observedAttackTypes == 2);
        var low = 0; var high = timelines.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var row = timelines[middle];
            var comparison = row.Key.AsSpan().SequenceCompareTo(characters);
            if (comparison < 0) low = middle + 1;
            else if (comparison > 0) high = middle - 1;
            else return new(row.Kinds == 2 ? EnemyActionKind.Skill : row.Kinds == 1 ? EnemyActionKind.AutoAttack : EnemyActionKind.Unknown,
                TimelineId: row.Id, Phase: Phase(row.Phases), HasAttackType: row.AttackTypes == 2);
        }
        // Some scripted executions have no cast or action-result callback.
        // Reuse game-data templates only when all matching rows agree. Actor
        // history and literal keys above remain authoritative, including their
        // unknown/auto classifications; never guess from the first wildcard.
        byte modelKinds = 0, modelPhases = 0, modelAttackTypes = 0;
        uint modelId = 0;
        foreach (var row in modelTimelines)
            if (Matches(row.Key, characters))
            {
                modelKinds |= row.Kinds; modelPhases |= row.Phases; modelAttackTypes |= row.AttackTypes;
                if (modelId == 0 || row.Id < modelId) modelId = row.Id;
            }
        return modelId == 0 ? default : new(modelKinds == 2 ? EnemyActionKind.Skill :
            modelKinds == 1 ? EnemyActionKind.AutoAttack : EnemyActionKind.Unknown,
            TimelineId: modelId, Phase: Phase(modelPhases), HasAttackType: modelAttackTypes == 2);
    }

    private static bool Matches(string template, ReadOnlySpan<char> actual)
    {
        if (actual.SequenceEqual(template)) return true;
        const string token = "[SKL_ID]";
        var at = template.IndexOf(token, StringComparison.Ordinal);
        if (at < 0) return false;
        var prefix = template.AsSpan(0, at); var suffix = template.AsSpan(at + token.Length);
        var length = actual.Length - prefix.Length - suffix.Length;
        if (length is < 1 or > 16 || !actual.StartsWith(prefix) || !actual.EndsWith(suffix)) return false;
        foreach (var c in actual.Slice(prefix.Length, length))
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        return true;
    }
}
