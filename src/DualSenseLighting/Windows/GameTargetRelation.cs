// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace DualSenseLighting.Windows;

/// <summary>Game-defined targeting relationships, queried on the framework thread with a live Character.</summary>
internal static unsafe class GameTargetRelation
{
    public static bool Available => ActionManager.MemberFunctionPointers.ClassifyTarget != null;

    /// <summary>The caller must establish Character type and current actor identity before supplying its address.</summary>
    public static int? Read(nint actor)
    {
        if (actor == 0) return null;
        try
        {
            if (!Available) return null;
            var category = (int)ActionManager.ClassifyTarget((Character*)actor);
            return category is >= 0 and <= 7 ? category : null;
        }
        catch { return null; } // An unavailable managed binding must not break roster publication.
    }

    public static bool IsEnemy(int? category) => category == (int)ActionManager.TargetCategory.Enemy;
}
