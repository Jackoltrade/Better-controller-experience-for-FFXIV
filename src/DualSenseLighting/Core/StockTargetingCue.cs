// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

/// <summary>The game's shared lock-on warning banks, independent of encounter or actor.</summary>
public static class StockTargetingCue
{
    public static bool Matches(ReadOnlySpan<byte> path)
    {
        ReadOnlySpan<byte> folder = "sound/vfx/monster"u8;
        ReadOnlySpan<byte> bank = "SE_Vfx_Monster_ETC_LockOn_"u8;
        ReadOnlySpan<byte> suffix = ".scd"u8;
        if (path.Length <= folder.Length + 1 + bank.Length + suffix.Length ||
            !EqualAscii(path[..folder.Length], folder)) return false;
        var offset = folder.Length;
        while (offset < path.Length && path[offset] is >= (byte)'0' and <= (byte)'9') offset++;
        if (offset >= path.Length || path[offset++] != (byte)'/' ||
            path.Length <= offset + bank.Length + suffix.Length ||
            !EqualAscii(path.Slice(offset, bank.Length), bank) ||
            !EqualAscii(path[^suffix.Length..], suffix)) return false;
        // A single bank filename only; mod paths, subfolders, and embedded NULs
        // must not accidentally match the shared stock marker family.
        foreach (var value in path[(offset + bank.Length)..^suffix.Length])
            if (!(value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or
                >= (byte)'0' and <= (byte)'9' or (byte)'_')) return false;
        return true;
    }

    private static bool EqualAscii(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            var value = left[i];
            if (value is >= (byte)'A' and <= (byte)'Z') value += (byte)('a' - 'A');
            var expected = right[i];
            if (expected is >= (byte)'A' and <= (byte)'Z') expected += (byte)('a' - 'A');
            if (value != expected) return false;
        }
        return true;
    }
}
