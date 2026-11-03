// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Threading;

namespace DualSenseLighting.Core;

/// <summary>Own casts and confirmed LB uses, scoped to the current actor, job and territory.</summary>
public sealed class LimitBreakTracker
{
    public const double InstantFlashSeconds = 0.8;
    private sealed record Actor(nint Address, uint EntityId, uint Territory, uint Job, bool IsPvP);
    private sealed record Execution(Actor Actor, uint Sequence, double Seconds);
    private readonly FrozenDictionary<uint, bool> actions;
    private Actor? actor;
    private Execution? execution;
    private uint castAction;
    private double castStartedAt;

    // Values distinguish PvE from PvP; action names and job-specific IDs are not hard-coded.
    public LimitBreakTracker(IReadOnlyDictionary<uint, bool> actions) => this.actions = actions.ToFrozenDictionary();

    // Framework thread only. Replacing the identity invalidates in-flight observations as well.
    public void SetActor(nint address, uint entityId, uint territory, uint job, bool pvp, bool enabled)
    {
        var previous = Volatile.Read(ref actor);
        var valid = enabled && address != 0 && entityId is not (0 or 0xE0000000);
        if (valid && previous is not null && previous.Address == address && previous.EntityId == entityId &&
            previous.Territory == territory && previous.Job == job && previous.IsPvP == pvp) return;
        Volatile.Write(ref actor, null);
        Volatile.Write(ref execution, null);
        Volatile.Write(ref castAction, 0);
        castStartedAt = 0;
        if (valid) Volatile.Write(ref actor, new Actor(address, entityId, territory, job, pvp));
    }

    private bool Matches(uint actionId, byte actionType, bool pvp) =>
        (actionType == 1 || pvp && actionType == 14) &&
        actions.TryGetValue(actionId, out var isPvpAction) && isPvpAction == pvp;

    // Native observer: immutable lookup and a single atomic publication; never calls game data or controller I/O.
    public void ObserveExecution(uint caster, uint actionId, byte actionType, uint sequence, double seconds)
    {
        var owner = Volatile.Read(ref actor);
        if (owner is null || owner.EntityId != caster || !double.IsFinite(seconds) ||
            !Matches(actionId, actionType, owner.IsPvP) || Volatile.Read(ref castAction) == actionId) return;
        var previous = Volatile.Read(ref execution);
        if (previous is not null && ReferenceEquals(previous.Actor, owner) &&
            unchecked((int)(sequence - previous.Sequence)) <= 0) return;
        Interlocked.CompareExchange(ref execution, new Execution(owner, sequence, seconds), previous);
    }

    // Framework thread only. Casting ends immediately when the game's cast flag clears.
    public LimitBreakState Sample(LimitBreakGauge gauge, bool casting, byte castType, uint castId, double seconds)
    {
        var owner = Volatile.Read(ref actor);
        if (owner is null || !gauge.Valid || (gauge.IsPvP != 0) != owner.IsPvP || !double.IsFinite(seconds))
        {
            Volatile.Write(ref castAction, 0);
            return LimitBreakState.Empty;
        }
        var activeCast = casting && Matches(castId, castType, owner.IsPvP);
        if (activeCast && Volatile.Read(ref castAction) != castId) castStartedAt = seconds;
        Volatile.Write(ref castAction, activeCast ? castId : 0);
        var used = Volatile.Read(ref execution);
        var flash = used is not null && ReferenceEquals(used.Actor, owner) &&
            seconds >= used.Seconds && seconds < used.Seconds + InstantFlashSeconds;
        return new(owner.IsPvP, gauge.BarCount, gauge.CurrentUnits, gauge.BarUnits, seconds, activeCast,
            activeCast ? castStartedAt : flash ? used!.Seconds : 0,
            !activeCast && flash ? used!.Seconds + InstantFlashSeconds : 0);
    }
}
