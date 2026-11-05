// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace DualSenseLighting.Core;

public readonly record struct EnemyHuntDefinition(uint NameId, uint BaseId, byte Rank);
public readonly record struct EnemyFateDefinition(uint Id, uint MapIcon, uint Icon);
public readonly record struct EnemyNpcRankDefinition(uint BaseId, byte Rank);

/// <summary>Duty, elite Hunt, and ranked boss FATE metadata.</summary>
public sealed class EnemyEncounterCatalog
{
    private readonly HashSet<ulong> hunts;
    private readonly HashSet<uint> bossFates, rankedNpcs;

    public EnemyEncounterCatalog(IEnumerable<EnemyHuntDefinition> hunts, IEnumerable<EnemyFateDefinition> fates,
        IEnumerable<EnemyNpcRankDefinition> npcs)
    {
        this.hunts = hunts.Where(x => x.NameId != 0 && x.BaseId != 0 && x.Rank is 1 or 2 or 3)
            .Select(x => ((ulong)x.NameId << 32) | x.BaseId).ToHashSet();
        // Standard boss FATE and critical-engagement map markers; use the
        // event icon too, because a few boss events have a generic map marker.
        bossFates = fates.Where(x => x.Id != 0 && (x.MapIcon is 60502 or 60802 || x.Icon is 60722 or 63915))
            .Select(x => x.Id).ToHashSet();
        // These ranks include elite/boss NPCs. Requiring an actual boss FATE
        // association keeps ordinary event mobs and nearby elites separate.
        rankedNpcs = npcs.Where(x => x.BaseId != 0 && x.Rank is 1 or 2 or 6).Select(x => x.BaseId).ToHashSet();
    }

    public bool IsBoss(uint duty, uint name, uint npcBase, ushort fate) =>
        EnemyBossCatalog.IsBoss(duty, name) || name != 0 && npcBase != 0 &&
        (hunts.Contains(((ulong)name << 32) | npcBase) || fate != 0 && bossFates.Contains(fate) && rankedNpcs.Contains(npcBase));

}
