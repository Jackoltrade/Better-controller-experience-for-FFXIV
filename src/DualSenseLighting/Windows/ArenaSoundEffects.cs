// SPDX-License-Identifier: GPL-3.0-only
// Read-only native layout fields adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Data.Parsing.Scd;

namespace DualSenseLighting.Windows;

/// <summary>Framework-only arena discovery; the mixer reads immutable results.</summary>
internal sealed class ArenaSoundEffects : IDisposable
{
    private const string WorldSignature = "48 8B D1 48 8B 0D ?? ?? ?? ?? 48 85 C9 74 0A";
    private const string MapGetterSignature = "4C 8B 91 68 02 00 00 49 8B CA 49 8B 42 08 80 78 19 00 75 17 38 50 20 73 06 48 8B 40 10 EB 06";
    private const int PoolCapacity = 256, SoundSize = 0xD0, MaximumMapNodes = 2048;
    private readonly IDataManager data;
    private readonly SpecialSoundPlayback playback;
    private readonly nint pool, moduleStart, moduleEnd, searchBase, worldGlobal;
    private readonly HashSet<nint> translationTables = new();
    private readonly string? startupError;
    private string? catalogError, nativeError;
    private Task<Catalog>? catalogTask;
    private bool catalogRequested;
    private Catalog catalog = Catalog.Empty;
    private uint territory = uint.MaxValue;
    private int loadGeneration, disposed, enabled;
    private long nextPoll, nextWalk;
    private nint cachedLayout;
    private Instance[] instances = Array.Empty<Instance>();
    private ArenaEffectSource[] published = Array.Empty<ArenaEffectSource>();

    private sealed record Catalog(Dictionary<string, HashSet<uint>> Sounds)
    { public static readonly Catalog Empty = new(new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase)); }
    private readonly record struct Instance(nint Address, ulong Identifier, uint SubId, nint SoundData, nint Table);
    private readonly record struct MapNode(ulong Key, nint Value);
    private readonly record struct Candidate(int Slot, nint Address, nint Handle, uint Number, int PlaybackId,
        nint System, int ObjectIndex, string Resource);

    public ArenaSoundEffects(ISigScanner scanner, IDataManager data, nint pool, SpecialSoundPlayback playback)
    {
        this.data = data; this.pool = pool; this.playback = playback;
        moduleStart = scanner.Module.BaseAddress;
        moduleEnd = checked(moduleStart + scanner.Module.ModuleMemorySize);
        searchBase = scanner.SearchBase;
        try
        {
            if (pool <= 0 || (pool & 7) != 0 || playback.Error != null) throw new NotSupportedException();
            var world = Unique(scanner, WorldSignature);
            Span<byte> body = stackalloc byte[15];
            if (!Read(Inspect(scanner, world, body.Length), body) ||
                !body[..6].SequenceEqual(new byte[] { 0x48, 0x8B, 0xD1, 0x48, 0x8B, 0x0D }) ||
                !body[10..].SequenceEqual(new byte[] { 0x48, 0x85, 0xC9, 0x74, 0x0A })) throw new NotSupportedException();
            worldGlobal = checked(world + 10 + Int(body, 6));
            if (!InModule(worldGlobal, 8)) throw new NotSupportedException();
            var getter = Unique(scanner, MapGetterSignature);
            Span<byte> map = stackalloc byte[7];
            if (!Read(Inspect(scanner, getter, map.Length), map) ||
                !map.SequenceEqual(new byte[] { 0x4C, 0x8B, 0x91, 0x68, 0x02, 0, 0 })) throw new NotSupportedException();
        }
        catch { startupError = "Arena effect layout is unsupported on this game build."; }
    }

    public string? Error => startupError ?? catalogError ?? nativeError;
    public ArenaEffectSource[] Sources => (ArenaEffectSource[])Volatile.Read(ref published).Clone();

    public ArenaEffectSource? Find(int positiveSoundId)
    {
        if (positiveSoundId <= 0 || Volatile.Read(ref enabled) == 0 || Volatile.Read(ref disposed) != 0) return null;
        foreach (var source in Volatile.Read(ref published))
            if (source.SoundId == positiveSoundId) return source;
        return null;
    }

    /// <summary>Called only by the framework, with the parent's active boss-combat gate.</summary>
    public void Update(uint currentTerritory, bool active, bool prepare = false)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        Volatile.Write(ref enabled, active ? 1 : 0);
        if (territory != currentTerritory)
        {
            Interlocked.Increment(ref loadGeneration);
            territory = currentTerritory;
            cachedLayout = 0; instances = Array.Empty<Instance>();
            catalog = Catalog.Empty; catalogError = null; nativeError = null;
            Publish(Array.Empty<ArenaEffectSource>());
            nextPoll = nextWalk = 0;
            catalogRequested = false;
        }
        // Prewarm only requested duty metadata, independently of the combat
        // output gate, so the first arena impact need not wait for asset IO.
        if (startupError == null && (active || prepare) && !catalogRequested)
        {
            catalogRequested = true;
            StartCatalog(currentTerritory);
        }
        if (catalogRequested && catalogTask?.IsCompleted == true)
        {
            var completed = catalogTask; catalogTask = null;
            try { catalog = completed.GetAwaiter().GetResult(); }
            catch (Exception error) { catalogError = error.Message; catalog = Catalog.Empty; }
        }
        if (!active || startupError != null)
        { Publish(Array.Empty<ArenaEffectSource>()); return; }
        var now = Stopwatch.GetTimestamp();
        if (now < nextPoll) return;
        nextPoll = now + Stopwatch.Frequency / 20;
        if (catalog.Sounds.Count == 0)
        { Publish(Array.Empty<ArenaEffectSource>()); return; }
        try
        {
            var candidates = Candidates();
            if (candidates.Count == 0)
            { Publish(Array.Empty<ArenaEffectSource>()); nativeError = null; return; }
            var world = Pointer(worldGlobal);
            var layout = Pointer(world + 0x20);
            Span<byte> layoutHeader = stackalloc byte[0x30];
            if (!Read(layout, layoutHeader) || Int(layoutHeader, 0x18) != 7 || UInt(layoutHeader, 0x20) != territory)
                throw new InvalidOperationException("Arena layout is not ready.");
            if (cachedLayout != layout)
            { cachedLayout = layout; instances = Array.Empty<Instance>(); nextWalk = 0; }
            if (now >= nextWalk)
            {
                instances = ReadInstances(layout);
                nextWalk = now + Stopwatch.Frequency / 10;
            }
            var result = new List<ArenaEffectSource>();
            var duplicate = new HashSet<int>();
            foreach (var candidate in candidates)
            {
                ArenaEffectSource? match = null;
                var ambiguous = false;
                foreach (var instance in instances)
                {
                    if (instance.SoundData != candidate.Address) continue;
                    if (!TrySource(layout, instance, candidate, out var source)) continue;
                    if (match.HasValue) { ambiguous = true; break; }
                    match = source;
                }
                if (!match.HasValue || ambiguous) continue;
                if (result.Any(x => x.SoundId == candidate.PlaybackId)) duplicate.Add(candidate.PlaybackId);
                else result.Add(match.Value);
            }
            if (Pointer(worldGlobal) != world || Pointer(world + 0x20) != layout ||
                !Read(layout, layoutHeader) || Int(layoutHeader, 0x18) != 7 || UInt(layoutHeader, 0x20) != territory)
                throw new InvalidOperationException("Arena scene changed during discovery.");
            result.RemoveAll(x => duplicate.Contains(x.SoundId));
            Publish(result.OrderBy(x => x.SoundId).ToArray());
            nativeError = null;
        }
        catch (Exception error)
        { Publish(Array.Empty<ArenaEffectSource>()); nativeError = error.Message; cachedLayout = 0; instances = Array.Empty<Instance>(); }
    }

    private void StartCatalog(uint wanted)
    {
        var generation = Interlocked.Increment(ref loadGeneration);
        var previous = catalogTask;
        // Lumina parsers hold mutable readers; serialize loads, including when
        // territory changes again before a previous catalog has finished.
        catalogTask = Task.Run(async () =>
        {
            if (previous != null) try { await previous.ConfigureAwait(false); } catch { }
            return LoadCatalog(wanted, generation);
        });
    }

    private Catalog LoadCatalog(uint wanted, int generation)
    {
        void Check()
        {
            if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref loadGeneration) != generation)
                throw new OperationCanceledException();
        }
        Check();
        var bg = data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(wanted)?.Bg.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(bg) || bg.LastIndexOf('/') < 0) return Catalog.Empty;
        var folder = (bg.StartsWith("bg/", StringComparison.OrdinalIgnoreCase) ? bg : "bg/" + bg);
        folder = folder[..(folder.LastIndexOf('/') + 1)];
        var lgb = data.GetFile<LgbFile>(folder + "vfx.lgb");
        if (lgb == null) return Catalog.Empty;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var groupFiles = new Dictionary<string, SgbFile?>(StringComparer.OrdinalIgnoreCase);
        var objects = 0;
        void Visit(IEnumerable<LayerCommon.Layer> layers, int depth)
        {
            foreach (var layer in layers)
                foreach (var obj in layer.InstanceObjects)
                {
                    Check();
                    if (++objects > 16384) throw new NotSupportedException("Arena VFX catalog exceeds the supported object bound.");
                    if (obj.Object is LayerCommon.SoundInstanceObject sound)
                    {
                        if (!string.IsNullOrWhiteSpace(sound.AssetPath)) paths.Add(sound.AssetPath);
                        if (paths.Count > 512) throw new NotSupportedException("Arena VFX catalog exceeds the supported sound bound.");
                    }
                    else if (obj.Object is LayerCommon.SharedGroupInstanceObject group && depth < 4 &&
                        !string.IsNullOrWhiteSpace(group.AssetPath))
                    {
                        if (groups.TryGetValue(group.AssetPath, out var previousDepth) && previousDepth <= depth + 1) continue;
                        groups[group.AssetPath] = depth + 1;
                        if (groups.Count > 128) throw new NotSupportedException("Arena VFX catalog exceeds the supported group bound.");
                        if (!groupFiles.TryGetValue(group.AssetPath, out var sgb))
                            groupFiles[group.AssetPath] = sgb = data.GetFile<SgbFile>(group.AssetPath);
                        if (sgb != null) foreach (var nested in sgb.LayerGroups) Visit(nested.Layers, depth + 1);
                    }
                }
        }
        Visit(lgb.Layers, 0);
        var banks = new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            Check();
            var file = data.GetFile<ScdFile>(path);
            if (file == null) continue;
            if (file.SoundDataCount is < 0 or > 4096) throw new NotSupportedException("Arena sound catalog exceeds the supported sound-index bound.");
            var numbers = new HashSet<uint>();
            for (var number = 0; number < file.SoundDataCount; number++)
            {
                Check();
                if ((file.GetSound(number).SoundBasicDesc.Attribute & SoundAttribute.Loop) == 0) numbers.Add((uint)number);
            }
            if (numbers.Count > 0) banks[path] = numbers;
        }
        return new(banks);
    }

    private List<Candidate> Candidates()
    {
        var result = new List<Candidate>();
        Span<byte> header = stackalloc byte[SoundSize];
        for (var slot = 0; slot < PoolCapacity; slot++)
        {
            var address = pool + slot * SoundSize;
            if (!Read(address, header)) throw new InvalidOperationException("Arena sound pool is unreadable.");
            var handle = Ptr(header, 0x28);
            var number = UInt(header, 0x7C);
            if (header[0x24] != 1 || handle <= 0 || Int(header, 0xB0) != 1) continue;
            var path = Resource(handle);
            if (path == null || !catalog.Sounds.TryGetValue(path, out var numbers) || !numbers.Contains(number) ||
                !playback.TryResolve(address, out var id)) continue;
            result.Add(new(slot, address, handle, number, id, Ptr(header, 0x90), Int(header, 0x98), path));
        }
        return result;
    }

    private Instance[] ReadInstances(nint layout)
    {
        var budget = MaximumMapNodes;
        var outer = Map(layout + 0x268, 128, ref budget);
        var groups = outer.Where(x => (byte)x.Key == 7).ToArray();
        if (groups.Length == 0) return Array.Empty<Instance>();
        if (groups.Length != 1) throw new InvalidOperationException("Arena sound map is ambiguous.");
        var result = new List<Instance>();
        Span<byte> header = stackalloc byte[0x80];
        foreach (var node in Map(groups[0].Value, MaximumMapNodes, ref budget))
        {
            if (!Read(node.Value, header) || Ptr(header, 0x10) != layout || header[0x19] != 7 || !TranslationLayout(Ptr(header)) ||
                node.Key != ((ulong)UInt(header, 0x1C) << 32 | UInt(header, 0x20)))
                throw new InvalidOperationException("Arena sound instance identity changed.");
            result.Add(new(node.Value, ULong(header, 0x18), UInt(header, 0x20), Ptr(header, 0x38), Ptr(header)));
        }
        return result.ToArray();
    }

    private bool TrySource(nint layout, Instance instance, Candidate candidate, out ArenaEffectSource source)
    {
        source = default;
        Span<byte> header = stackalloc byte[0x80];
        if (!Read(instance.Address, header) || Ptr(header, 0x10) != layout || ULong(header, 0x18) != instance.Identifier ||
            UInt(header, 0x20) != instance.SubId || header[0x19] != 7 || (header[0x2B] & 0x10) == 0 ||
            Ptr(header, 0x38) != candidate.Address || Ptr(header, 0x48) != candidate.Handle || Ptr(header) != instance.Table) return false;
        Span<byte> current = stackalloc byte[SoundSize];
        if (!Read(candidate.Address, current) || current[0x24] != 1 || Ptr(current, 0x28) != candidate.Handle ||
            UInt(current, 0x7C) != candidate.Number || Int(current, 0xB0) != 1 ||
            Ptr(current, 0x90) != candidate.System || Int(current, 0x98) != candidate.ObjectIndex ||
            !playback.TryResolve(candidate.Address, out var playbackId) || playbackId != candidate.PlaybackId) return false;
        Span<byte> after = stackalloc byte[0x80];
        if (!Read(instance.Address, after) || Ptr(after) != instance.Table || Ptr(after, 0x10) != layout ||
            ULong(after, 0x18) != instance.Identifier || UInt(after, 0x20) != instance.SubId ||
            (after[0x2B] & 0x10) == 0 || Ptr(after, 0x38) != candidate.Address || Ptr(after, 0x48) != candidate.Handle) return false;
        var position = new Vector3(Float(after, 0x50), Float(after, 0x54), Float(after, 0x58));
        if (!HapticSpatializer.ValidPosition(position)) return false;
        source = new(playbackId, position, candidate.Resource, candidate.Number, candidate.Slot,
            $"{candidate.Address:X}", $"{instance.Address:X}", UInt(header, 0x1C), instance.SubId);
        return true;
    }

    private bool TranslationLayout(nint table)
    {
        if (translationTables.Contains(table)) return true;
        if (translationTables.Count >= 16 || !InModule(table, 65 * 8)) return false;
        var getter = Pointer(table + 64 * 8);
        var original = GameAudioLayout.InspectionAddress(getter, moduleStart, searchBase, (int)(moduleEnd - moduleStart), 5);
        Span<byte> body = stackalloc byte[5];
        if (original == 0 || !Read(original, body) ||
            !body.SequenceEqual(new byte[] { 0x48, 0x8D, 0x41, 0x50, 0xC3 })) return false;
        translationTables.Add(table);
        return true;
    }

    private List<MapNode> Map(nint address, int maximum, ref int budget)
    {
        Span<byte> header = stackalloc byte[16];
        if (!Read(address, header)) throw new InvalidOperationException("Arena map header is unreadable.");
        var root = Ptr(header);
        var count = ULong(header, 8);
        if (root <= 0 || count > (ulong)maximum || count > (ulong)budget) throw new InvalidOperationException("Arena map exceeds its node bound.");
        budget -= (int)count;
        Span<byte> node = stackalloc byte[48];
        if (!Read(root, node) || node[25] != 1) throw new InvalidOperationException("Arena map sentinel is invalid.");
        var todo = new Stack<nint>(); var seen = new HashSet<nint>(); var result = new List<MapNode>();
        todo.Push(Ptr(node, 8));
        while (todo.Count > 0)
        {
            var at = todo.Pop();
            if (at == root) continue;
            if (at == 0 || !seen.Add(at) || result.Count >= (int)count || !Read(at, node) || node[25] != 0)
                throw new InvalidOperationException("Arena map changed during discovery.");
            result.Add(new(ULong(node, 32), Ptr(node, 40)));
            todo.Push(Ptr(node)); todo.Push(Ptr(node, 16));
        }
        Span<byte> after = stackalloc byte[16];
        if ((ulong)result.Count != count || !Read(address, after) || !header.SequenceEqual(after))
            throw new InvalidOperationException("Arena map header changed during discovery.");
        return result;
    }

    private string? Resource(nint handle)
    {
        Span<byte> header = stackalloc byte[32];
        if (!Read(handle + 0x48, header)) return null;
        var length = ULong(header, 16); var capacity = ULong(header, 24);
        if (length is 0 or > 1024 || capacity < length) return null;
        Span<byte> bytes = stackalloc byte[1024];
        var address = capacity < 16 ? handle + 0x48 : Ptr(header);
        if (!Read(address, bytes[..(int)length])) return null;
        Span<byte> after = stackalloc byte[32];
        return Read(handle + 0x48, after) && header.SequenceEqual(after)
            ? Encoding.UTF8.GetString(bytes[..(int)length]) : null;
    }

    private void Publish(ArenaEffectSource[] next)
    {
        var previous = Volatile.Read(ref published);
        if (!previous.AsSpan().SequenceEqual(next)) Volatile.Write(ref published, next);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref enabled, 0); Interlocked.Increment(ref loadGeneration);
        Publish(Array.Empty<ArenaEffectSource>());
        try { catalogTask?.GetAwaiter().GetResult(); } catch { }
    }

    private static nint Unique(ISigScanner scanner, string signature)
    { var matches = scanner.ScanAllText(signature); return matches.Length == 1 ? matches[0] : throw new NotSupportedException(); }
    private nint Inspect(ISigScanner scanner, nint address, int length) =>
        GameAudioLayout.InspectionAddress(address, moduleStart, scanner.SearchBase, scanner.Module.ModuleMemorySize, length);
    private bool InModule(nint address, int length) => address >= moduleStart && address > 0 && address <= moduleEnd - length;
    private static int Int(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private static uint UInt(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static ulong ULong(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);
    private static nint Ptr(ReadOnlySpan<byte> bytes, int offset = 0) => (nint)BinaryPrimitives.ReadInt64LittleEndian(bytes[offset..]);
    private static float Float(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]);
    private static nint Pointer(nint address)
    { Span<byte> bytes = stackalloc byte[8]; return Read(address, bytes) ? Ptr(bytes) : 0; }
    [DllImport("kernel32.dll")]
    private static extern unsafe bool ReadProcessMemory(nint process, nint address, void* bytes, nuint size, out nuint read);
    private static unsafe bool Read(nint address, Span<byte> bytes)
    {
        if (address <= 0 || bytes.Length == 0 || address > nint.MaxValue - bytes.Length) return false;
        fixed (byte* target = bytes)
            return ReadProcessMemory(-1, address, target, (nuint)bytes.Length, out var read) && read == (nuint)bytes.Length;
    }
}
