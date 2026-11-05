// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>Copies a bounded resource filename without dereferencing a disposable native string.</summary>
internal static unsafe class EnemySoundCueFilter
{
    private const int MaximumPath = 1024;

    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(nint process, nint address, void* data, nuint size, out nuint read);

    public static bool? Read(nint resourceHandle)
    {
        if (resourceHandle <= 0 || resourceHandle > nint.MaxValue - 0x68) return null;
        Span<byte> header = stackalloc byte[32];
        if (!Copy(resourceHandle + 0x48, header)) return null;
        var length = BinaryPrimitives.ReadUInt64LittleEndian(header[16..]);
        var capacity = BinaryPrimitives.ReadUInt64LittleEndian(header[24..]);
        if (length is 0 or > MaximumPath || capacity < length) return null;
        if (capacity < 16) return StockTargetingCue.Matches(header[..(int)length]);
        var address = BinaryPrimitives.ReadUInt64LittleEndian(header);
        if (address == 0 || address > (ulong)nint.MaxValue - length) return null;
        Span<byte> path = stackalloc byte[MaximumPath];
        if (!Copy((nint)address, path[..(int)length])) return null;
        // Async loading or resource disposal can change the std::string while
        // copied. A changed header is unknown and will be retried on a later block.
        Span<byte> current = stackalloc byte[32];
        if (!Copy(resourceHandle + 0x48, current) || !header.SequenceEqual(current)) return null;
        return StockTargetingCue.Matches(path[..(int)length]);
    }

    private static bool Copy(nint address, Span<byte> destination)
    {
        fixed (byte* data = destination)
            return ReadProcessMemory(-1, address, data, (nuint)destination.Length, out var read) &&
                read == (nuint)destination.Length;
    }
}
