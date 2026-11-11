// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
namespace DualSenseLighting.Core;

// Admission only: never caches native attribution or alters original playback.
internal static class SfxCapturePolicy
{
    internal static bool NeedsVoice(bool lighting, HapticSettings? settings, bool enemies, bool hitReactions, bool footsteps) =>
        lighting || footsteps || enemies || hitReactions || settings is { Enabled: true, Strength: > 0 };

    internal static bool NeedsOwnership(bool combat, uint flags, bool playerLighting, HapticSettings? settings,
        bool enemies, bool hitReactions, bool arenaEffects = false) => combat && playerLighting ||
        (combat || arenaEffects) && (flags & 0xE) == 0 &&
        (enemies || hitReactions || settings is { Enabled: true, Strength: > 0 });
}
