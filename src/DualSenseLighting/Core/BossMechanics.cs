// SPDX-License-Identifier: GPL-3.0-only
namespace DualSenseLighting.Core;

/// <summary>Encounter associations for effect NPCs without a native owner. See data/README.md.</summary>
public static class BossMechanics
{
    public static bool HasOwner(uint owner) => owner is not (0 or 0xE0000000);

    // All three IDs must match; a live matching boss is additionally required by SelectEnemyActors.
    // This associates the actor, not a sound filename or a recently cast skill.
    public static uint OwnerName(uint duty, uint npcName, uint npcBase) =>
        (duty, npcName, npcBase) switch
        {
            // The Heroes' Gauntlet: Spectral Thief's summoned Chicken Knife.
            (737, 9507, 11889) => 9505,
            // Spectral Necromancer's two Necrobomb variants, four base entries each.
            (737, 9509, 11766 or 11767 or 11768 or 11769) => 9508,
            (737, 9510, 11762 or 11763 or 11764 or 11765) => 9508,
            // Spectral Berserker's destructible rubble and its fall/destruction effects.
            (737, 9512, 12030) => 9511,
            _ => 0
        };
}
