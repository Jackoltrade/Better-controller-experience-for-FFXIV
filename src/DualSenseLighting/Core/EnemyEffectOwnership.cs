// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System.Threading;

namespace DualSenseLighting.Core;

/// <summary>Preserves a skill's ownership across its native visual effect's lifetime.</summary>
public sealed class EnemyEffectOwnership
{
    private const int Capacity = 4096;
    private sealed record Entry(nint Listener, nint Instance, GameAudioLayout.PlayerIdentity Caster, EnemyActionTag Tag);
    private readonly Entry?[] entries = new Entry?[Capacity];
    private static int Slot(nint listener) => (int)((nuint)listener >> 4) & (Capacity - 1);

    public void Forget(nint listener)
    {
        if (listener == 0) return;
        var slot = Slot(listener);
        var entry = Volatile.Read(ref entries[slot]);
        if (entry?.Listener == listener) Interlocked.CompareExchange(ref entries[slot], null, entry);
    }

    public void Observe(nint listener, nint instance, GameAudioLayout.PlayerIdentity? caster, EnemyActionTag timeline)
    {
        if (listener == 0) return;
        var tag = EnemySkillCatalog.TimelineEffect(timeline);
        if (instance == 0 || caster == null || caster.Actor == 0 || !tag.IsReleasedEffect)
        { Forget(listener); return; }
        var slot = Slot(listener);
        var previous = Volatile.Read(ref entries[slot]);
        // Timelines query the same effect repeatedly. Allocate only when its
        // association changes, never for lookup or every animation tick.
        if (previous?.Listener == listener && previous.Instance == instance &&
            ReferenceEquals(previous.Caster, caster) && previous.Tag == tag) return;
        Volatile.Write(ref entries[slot], new Entry(listener, instance, caster, tag));
    }

    public EnemyActionTag Resolve(nint listener, nint instance, GameAudioLayout.PlayerIdentity? caster)
    {
        if (listener == 0 || instance == 0 || caster == null) return default;
        var entry = Volatile.Read(ref entries[Slot(listener)]);
        return entry?.Listener == listener && entry.Instance == instance && ReferenceEquals(entry.Caster, caster)
            ? entry.Tag : default;
    }
}
