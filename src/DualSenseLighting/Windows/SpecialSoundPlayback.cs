// SPDX-License-Identifier: GPL-3.0-only
// Read-only native sound layout adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>Resolves the separate layout backend used by initialized environment SoundData.</summary>
internal sealed unsafe class SpecialSoundPlayback
{
    private const string EngineGetterCall = "E8 ?? ?? ?? ?? 44 8B 45 08 48 8D 54 24 20 48 8B 08 4C 8B 89 48 01 00 00";
    private const int MaximumLayouts = 128, MaximumListEntries = 512, MaximumLinkOffset = 256;
    private readonly nint engineGlobal, moduleStart, moduleEnd, engineVtable;
    public string? Error { get; }

    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(nint process, nint address, void* data, nuint size, out nuint read);

    public SpecialSoundPlayback(ISigScanner scanner)
    {
        try
        {
            moduleStart = scanner.Module.BaseAddress;
            moduleEnd = checked(moduleStart + scanner.Module.ModuleMemorySize);
            var matches = scanner.ScanAllText(EngineGetterCall);
            if (matches.Length != 1) throw new NotSupportedException();
            var code = Inspect(matches[0], scanner.SearchBase, 5);
            Span<byte> call = stackalloc byte[8];
            if (!Copy(code, call[..5]) || call[0] != 0xE8) throw new NotSupportedException();
            var getter = checked(matches[0] + 5 + Int(call, 1));
            if (!Copy(Inspect(getter, scanner.SearchBase, 8), call) ||
                call[0] != 0x48 || call[1] != 0x8B || call[2] != 0x05 || call[7] != 0xC3)
                throw new NotSupportedException();
            engineGlobal = checked(getter + 7 + Int(call, 3));
            if (!ValidateEngine(scanner.SearchBase, out engineVtable)) throw new NotSupportedException();
        }
        catch
        {
            Error = "Arena sound playback layout is unsupported on this game build.";
        }
    }

    // Uses the same RPM and layout validation with an unmanaged fixture; no game calls are needed.
    internal SpecialSoundPlayback(nint engineGlobalAddress, nint moduleStart, nint moduleEnd)
    {
        this.engineGlobal = engineGlobalAddress;
        this.moduleStart = moduleStart;
        this.moduleEnd = moduleEnd;
        if (!ValidateEngine(moduleStart, out engineVtable))
            Error = "Arena sound playback layout is unsupported on this game build.";
    }

    public bool TryResolve(nint soundData, out int playbackId)
    {
        playbackId = 0;
        if (Error != null) return false;
        Span<byte> sound = stackalloc byte[0xD0];
        if (!Copy(soundData, sound) || sound[0x24] != 1 || Ptr(sound, 0x28) <= 0 ||
            Int(sound, 0xB0) != 1 || Int(sound, 0x98) < 0) return false;

        // This backend does not initialize the embedded controller at +0x38.
        // Its ID at +0x40 can still belong to an unrelated previous pool occupant.
        var system = Ptr(sound, 0x90);
        Span<byte> systemHeader = stackalloc byte[16];
        if (!Copy(system, systemHeader) || Int(systemHeader, 8) <= 0) return false;
        var systemId = Int(systemHeader, 8);
        var requestedId = Int(sound, 0x98);

        Span<byte> engineHeader = stackalloc byte[0xB0];
        if (!ReadPointer(engineGlobal, out var engine) || !Copy(engine, engineHeader) ||
            Ptr(engineHeader) != engineVtable) return false;
        var count = Int(engineHeader, 0x68);
        var array = Ptr(engineHeader, 0xA8);
        if (count is < 1 or > MaximumLayouts) return false;
        Span<byte> layouts = stackalloc byte[MaximumLayouts * 8];
        layouts = layouts[..(count * 8)];
        if (!Copy(array, layouts)) return false;

        nint layout = 0;
        Span<byte> layoutHeader = stackalloc byte[0x20];
        for (var i = 0; i < count; i++)
        {
            var candidate = Ptr(layouts, i * 8);
            if (!Copy(candidate, layoutHeader)) return false;
            if (Int(layoutHeader, 0x18) != systemId) continue;
            if (layout != 0) return false;
            layout = candidate;
        }
        if (layout == 0) return false;

        Span<byte> listHeaders = stackalloc byte[3 * 0x20];
        Span<byte> nodeHeader = stackalloc byte[MaximumLinkOffset + 16];
        Span<byte> entryHeader = stackalloc byte[0x40];
        Span<byte> matchedHeader = stackalloc byte[0x40];
        nint matchedEntry = 0;
        var result = 0;
        for (var listIndex = 0; listIndex < 3; listIndex++)
        {
            var header = listHeaders.Slice(listIndex * 0x20, 0x20);
            if (!CopyAt(layout, 0x48 + listIndex * 0x20, header)) return false;
            var entries = Int(header, 0x10);
            var linkOffset = Ptr(header, 0x18);
            if (entries is < 0 or > MaximumListEntries || linkOffset < 0 ||
                linkOffset > MaximumLinkOffset || (linkOffset & 7) != 0) return false;
            var node = Ptr(header);
            nint last = 0;
            for (var i = 0; i < entries; i++)
            {
                last = node;
                if (!Copy(node, nodeHeader[..((int)linkOffset + 16)])) return false;
                var entry = Ptr(nodeHeader);
                if (!Copy(entry, entryHeader)) return false;
                if (Int(entryHeader, 0x38) == requestedId)
                {
                    if (matchedEntry != 0 || Ptr(entryHeader, 0x30) != layout) return false;
                    result = Int(entryHeader, 0x10);
                    if (result <= 0 || !Contains(layouts, Ptr(entryHeader, 0x18))) return false;
                    matchedEntry = entry;
                    entryHeader.CopyTo(matchedHeader);
                }
                node = Ptr(nodeHeader, (int)linkOffset + 8);
            }
            // A short list, cycle, or changing node count is unknown.
            if (node != 0 || last != Ptr(header, 8)) return false;
        }
        if (matchedEntry == 0) return false;

        Span<byte> current = stackalloc byte[0xD0];
        if (!Copy(soundData, current) || !SameSound(sound, current) ||
            !Copy(system, current[..16]) || Int(current, 8) != systemId ||
            !Copy(matchedEntry, current[..0x40]) || !SameEntry(matchedHeader, current) ||
            !Copy(layout, current[..0x20]) || Int(current, 0x18) != systemId) return false;
        for (var i = 0; i < 3; i++)
        {
            if (!CopyAt(layout, 0x48 + i * 0x20, current[..0x20]) ||
                !SameList(listHeaders.Slice(i * 0x20, 0x20), current)) return false;
        }
        Span<byte> currentLayouts = stackalloc byte[MaximumLayouts * 8];
        if (!Copy(array, currentLayouts[..layouts.Length]) || !layouts.SequenceEqual(currentLayouts[..layouts.Length]) ||
            !Copy(engine, current[..0xB0]) || Ptr(current) != engineVtable ||
            Int(current, 0x68) != count || Ptr(current, 0xA8) != array ||
            !ReadPointer(engineGlobal, out var currentEngine) || currentEngine != engine) return false;
        playbackId = result;
        return true;
    }

    private bool ValidateEngine(nint searchBase, out nint vtable)
    {
        vtable = 0;
        if (!InModule(engineGlobal, 8) || !ReadPointer(engineGlobal, out var engine) ||
            !ReadPointer(engine, out var table) || !InModule(table, 0x148) ||
            !ReadPointer(table + 0x140, out var lookup)) return false;
        Span<byte> body = stackalloc byte[0x50];
        if (!Copy(Inspect(lookup, searchBase, body.Length), body) ||
            !body.Slice(0x2D, 4).SequenceEqual(new byte[] { 0x48, 0x63, 0x45, 0x68 }) ||
            !body.Slice(0x3A, 7).SequenceEqual(new byte[] { 0x4C, 0x8B, 0x8D, 0xA8, 0, 0, 0 }) ||
            !body.Slice(0x4A, 3).SequenceEqual(new byte[] { 0x39, 0x70, 0x18 })) return false;
        vtable = table;
        return true;
    }

    private nint Inspect(nint address, nint searchBase, int length) =>
        moduleStart > 0 && moduleEnd > moduleStart && moduleEnd - moduleStart <= int.MaxValue
            ? GameAudioLayout.InspectionAddress(address, moduleStart, searchBase, (int)(moduleEnd - moduleStart), length) : 0;

    private bool InModule(nint address, int length) =>
        moduleStart > 0 && moduleEnd > moduleStart && length > 0 &&
        address >= moduleStart && address <= moduleEnd - length;

    private static bool Contains(ReadOnlySpan<byte> pointers, nint pointer)
    {
        if (pointer == 0) return false;
        for (var i = 0; i < pointers.Length; i += 8)
            if (Ptr(pointers, i) == pointer) return true;
        return false;
    }

    private static bool SameSound(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after) =>
        after[0x24] == 1 && Ptr(before, 0x28) == Ptr(after, 0x28) &&
        Int(before, 0x7C) == Int(after, 0x7C) && Ptr(before, 0x90) == Ptr(after, 0x90) &&
        Int(before, 0x98) == Int(after, 0x98) && Int(after, 0xB0) == 1;

    private static bool SameEntry(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after) =>
        Ptr(before) == Ptr(after) && Ptr(before, 8) == Ptr(after, 8) &&
        Int(before, 0x10) == Int(after, 0x10) && Ptr(before, 0x18) == Ptr(after, 0x18) &&
        Ptr(before, 0x28) == Ptr(after, 0x28) && Ptr(before, 0x30) == Ptr(after, 0x30) &&
        Int(before, 0x38) == Int(after, 0x38);

    private static bool SameList(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after) =>
        Ptr(before) == Ptr(after) && Ptr(before, 8) == Ptr(after, 8) &&
        Int(before, 0x10) == Int(after, 0x10) && Ptr(before, 0x18) == Ptr(after, 0x18);

    private static int Int(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static nint Ptr(ReadOnlySpan<byte> bytes, int offset = 0) => (nint)BinaryPrimitives.ReadInt64LittleEndian(bytes[offset..]);

    private static bool ReadPointer(nint address, out nint pointer)
    {
        Span<byte> bytes = stackalloc byte[8];
        var copied = Copy(address, bytes);
        pointer = copied ? Ptr(bytes) : 0;
        return copied && pointer > 0;
    }

    private static bool CopyAt(nint address, int offset, Span<byte> destination) =>
        address > 0 && offset >= 0 && address <= nint.MaxValue - offset && Copy(address + offset, destination);

    private static bool Copy(nint address, Span<byte> destination)
    {
        if (address <= 0 || destination.Length == 0 || address > nint.MaxValue - destination.Length) return false;
        fixed (byte* data = destination)
            return ReadProcessMemory(-1, address, data, (nuint)destination.Length, out var read) &&
                read == (nuint)destination.Length;
    }
}
