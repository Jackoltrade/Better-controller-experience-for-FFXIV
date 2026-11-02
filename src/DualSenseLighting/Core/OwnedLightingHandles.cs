// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System.Collections.Concurrent;

namespace DualSenseLighting.Core;

// Only the plugin's shared output handles bypass the game's report guard.
// This permits a single lights-off update after focus loss without allowing native
// FFXIV lighting commands through in the background.
internal static class OwnedLightingHandles
{
    private static readonly ConcurrentDictionary<nint, byte> Handles = new();
    internal static void Add(nint handle) => Handles.TryAdd(handle, 0);
    internal static void Remove(nint handle) => Handles.TryRemove(handle, out _);
    internal static bool Contains(nint handle) => Handles.ContainsKey(handle);
}
