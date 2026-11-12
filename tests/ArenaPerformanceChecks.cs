// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

// Calls the compiled arena pipeline using owned native layouts and the existing
// special-playback fixture. No hooks, game objects or hardware writes are used.
internal static unsafe class ArenaPerformanceChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var h = new Harness(plugin);
        h.Update();
        check(h.Published.Length == 1 && h.SoundId == 492626514 && h.Position == new Vector3(1, 2, 3),
            "Bulk arena discovery lost the exact playback/instance join or position");
        var source = h.Published.GetValue(0)!;
        var address = (string)source.GetType().GetProperty("SoundData")!.GetValue(source)!;
        check(ProductionBoundaryChecks.Diagnostics(plugin) ? address.Length > 0 : address.Length == 0,
            "Arena address formatting leaked into production or disappeared from diagnostics");
        var previous = h.Published;
        var scratch = h.Scratch;
        for (var i = 0; i < 5; i++) h.Update();
        check(ReferenceEquals(previous, h.Published) && scratch.Zip(h.Scratch).All(pair => ReferenceEquals(pair.First, pair.Second)),
            "Stable arena polls replaced the immutable publication or framework scratch collections");

        // Capture candidates, then reuse the pool slot before live acceptance.
        h.Call("Candidates");
        var candidate = ((IList)h.Get("candidates"))[0]!;
        var instance = ((IList)h.Get("instances"))[0]!;
        var originalNumber = *(uint*)(h.Pool + 0x7C);
        foreach (var field in new[] { "number", "handle", "active", "playback", "instance", "position" })
        {
            var handle = *(nint*)(h.Pool + 0x28);
            var active = h.Pool[0x24]; var playback = *(int*)(h.Special.Entry(0) + 0x10);
            var instanceLayout = *(nint*)(h.Instance + 0x10); var position = *(Vector3*)(h.Instance + 0x50);
            switch (field)
            {
                case "number": *(uint*)(h.Pool + 0x7C) = originalNumber + 1; break;
                case "handle": *(nint*)(h.Pool + 0x28) = 0; break;
                case "active": h.Pool[0x24] = 0; break;
                case "playback": *(int*)(h.Special.Entry(0) + 0x10) = playback + 1; break;
                case "instance": *(nint*)(h.Instance + 0x10) = 0; break;
                case "position": *(Vector3*)(h.Instance + 0x50) = new(float.NaN, 0, 0); break;
            }
            check(!h.TrySource(instance, candidate), "A copied arena candidate bypassed live " + field + " validation");
            *(uint*)(h.Pool + 0x7C) = originalNumber; *(nint*)(h.Pool + 0x28) = handle;
            h.Pool[0x24] = active; *(int*)(h.Special.Entry(0) + 0x10) = playback;
            *(nint*)(h.Instance + 0x10) = instanceLayout; *(Vector3*)(h.Instance + 0x50) = position;
        }
        check(h.TrySource(instance, candidate), "Restored live arena candidate no longer validates");

        // Every error must retire the last publication instead of playing stale output.
        h.ProtectPool(false);
        try
        {
            h.Update();
            check(h.Published.Length == 0 && ((string?)h.Get("nativeError"))?.Contains("pool") == true,
                "A partial/unreadable bulk pool read retained stale arena output");
        }
        finally { h.ProtectPool(true); }
        h.Update(); check(h.Published.Length == 1, "Arena discovery failed to recover after a readable pool returned");
        *(uint*)(h.Layout + 0x20) = 101;
        h.Update(); check(h.Published.Length == 0, "A changed scene territory retained arena sources");
        *(uint*)(h.Layout + 0x20) = 100;
        h.Update(); check(h.Published.Length == 1, "Restoring the matching arena scene failed");

        // Two distinct native slots can resolve the same playback ID. Neither wins.
        new ReadOnlySpan<byte>(h.Pool, 0xD0).CopyTo(new Span<byte>(h.Pool + 0xD0, 0xD0));
        var extraInstance = h.Buffer(0x80);
        new ReadOnlySpan<byte>(h.Instance, 0x80).CopyTo(new Span<byte>(extraInstance, 0x80));
        *(nint*)(extraInstance + 0x38) = (nint)(h.Pool + 0xD0);
        var extraNode = h.Buffer(48);
        h.InitializeNode(extraNode, h.InnerRoot, ((ulong)10 << 32) | 12, (nint)extraInstance);
        *(uint*)(extraInstance + 0x20) = 12;
        *(nint*)(h.InnerNode + 16) = (nint)extraNode; *(ulong*)(h.InnerMap + 8) = 2;
        h.Update(); check(h.Published.Length == 0, "Duplicate arena playback IDs were accepted by arbitrary iteration order");
        h.Pool[0xD0 + 0x24] = 0;
        *(nint*)(extraInstance + 0x38) = (nint)h.Pool;
        h.Update(); check(h.Published.Length == 0, "Ambiguous native instances were accepted for one arena sound");
        *(nint*)(h.InnerNode + 16) = (nint)h.InnerRoot; *(ulong*)(h.InnerMap + 8) = 1;
        h.Update(); check(h.Published.Length == 1, "Arena matching did not recover after ambiguity was removed");
        *(nint*)(h.InnerNode + 16) = (nint)h.InnerNode;
        h.Update(); check(h.Published.Length == 0, "A cyclic arena map retained stale sources after scratch reuse");
        *(nint*)(h.InnerNode + 16) = (nint)h.InnerRoot;
        h.Update(); check(h.Published.Length == 1, "A repaired arena map remained poisoned by scratch state");
    }

    private sealed class Harness : IDisposable
    {
        internal readonly SpecialSoundPlaybackChecks.Fixture Special = new();
        internal readonly byte* Pool;
        internal readonly byte* Layout, Instance, InnerMap, InnerRoot, InnerNode;
        private readonly List<nint> buffers = new();
        private readonly object arena;
        private readonly Type type;
        internal byte* Buffer(int size)
        {
            var value = (byte*)NativeMemory.AllocZeroed((nuint)size);
            buffers.Add((nint)value); return value;
        }
        internal Harness(Assembly plugin)
        {
            Pool = (byte*)VirtualAlloc(0, 0xD000, 0x3000, 4);
            if (Pool == null) throw new InvalidOperationException("Cannot allocate arena pool fixture");
            new ReadOnlySpan<byte>(Special.Sound, 0xD0).CopyTo(new Span<byte>(Pool, 0xD0));
            type = plugin.GetType("DualSenseLighting.Windows.ArenaSoundEffects", true)!;
            arena = RuntimeHelpers.GetUninitializedObject(type);
            foreach (var field in new[] { "candidates", "instances", "sources", "duplicateIds", "mapNodes", "mapTodo", "mapSeen", "translationTables" })
                Set(field, Activator.CreateInstance(type.GetField(field, Hidden)!.FieldType)!);
            Set("poolSnapshot", new byte[0xD000]);
            Set("published", Array.CreateInstance(type.GetField("published", Hidden)!.FieldType.GetElementType()!, 0));
            Set("pool", (nint)Pool); Set("territory", 100u); Set("catalogRequested", true);
            Set("moduleStart", (nint)Special.Module); Set("moduleEnd", (nint)(Special.Module + 4096)); Set("searchBase", (nint)Special.Module);
            var catalogType = type.GetNestedType("Catalog", BindingFlags.NonPublic)!;
            Set("catalog", Activator.CreateInstance(catalogType, new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase)
                { ["sound/test.scd"] = new() { 7 } })!);
            var playbackType = plugin.GetType("DualSenseLighting.Windows.SpecialSoundPlayback", true)!;
            Set("playback", playbackType.GetConstructor(Hidden, null, new[] { typeof(nint), typeof(nint), typeof(nint) }, null)!
                .Invoke(new object[] { (nint)Special.Global, (nint)Special.Module, (nint)(Special.Module + 4096) }));
            var handle = Buffer(0x80); var text = Encoding.UTF8.GetBytes("sound/test.scd");
            text.CopyTo(new Span<byte>(handle + 0x48, text.Length));
            *(ulong*)(handle + 0x58) = (ulong)text.Length; *(ulong*)(handle + 0x60) = 15;
            *(nint*)(Pool + 0x28) = (nint)handle; *(uint*)(Pool + 0x7C) = 7;
            Layout = Buffer(0x280); var world = Buffer(0x28); var global = Buffer(8);
            *(nint*)global = (nint)world; *(nint*)(world + 0x20) = (nint)Layout; Set("worldGlobal", (nint)global);
            *(int*)(Layout + 0x18) = 7; *(uint*)(Layout + 0x20) = 100;
            Instance = Buffer(0x80); var table = Special.Module + 0x600; var getter = Special.Module + 0xA00;
            *(nint*)(table + 64 * 8) = (nint)getter;
            new byte[] { 0x48, 0x8D, 0x41, 0x50, 0xC3 }.CopyTo(new Span<byte>(getter, 5));
            *(nint*)Instance = (nint)table; *(nint*)(Instance + 0x10) = (nint)Layout;
            Instance[0x19] = 7; *(uint*)(Instance + 0x1C) = 10; *(uint*)(Instance + 0x20) = 11; Instance[0x2B] = 0x10;
            *(nint*)(Instance + 0x38) = (nint)Pool; *(nint*)(Instance + 0x48) = (nint)handle;
            *(Vector3*)(Instance + 0x50) = new(1, 2, 3);
            InnerMap = Buffer(16); InnerRoot = Buffer(48); InnerNode = Buffer(48);
            InitializeMap(InnerMap, InnerRoot, InnerNode);
            InitializeNode(InnerNode, InnerRoot, ((ulong)10 << 32) | 11, (nint)Instance);
            var outerRoot = Buffer(48); var outerNode = Buffer(48);
            InitializeMap(Layout + 0x268, outerRoot, outerNode);
            InitializeNode(outerNode, outerRoot, 7, (nint)InnerMap);
        }
        private static void InitializeMap(byte* map, byte* root, byte* node)
        { *(nint*)map = (nint)root; *(ulong*)(map + 8) = 1; root[25] = 1; *(nint*)(root + 8) = (nint)node; }
        internal void InitializeNode(byte* node, byte* root, ulong key, nint value)
        { *(nint*)node = *(nint*)(node + 16) = (nint)root; *(ulong*)(node + 32) = key; *(nint*)(node + 40) = value; }
        internal object Get(string field) => type.GetField(field, Hidden)!.GetValue(arena)!;
        private void Set(string field, object value) => type.GetField(field, Hidden)!.SetValue(arena, value);
        internal object? Call(string method, params object?[] values) => type.GetMethod(method, Hidden)!.Invoke(arena, values);
        internal void Update() { Set("nextPoll", 0L); Set("nextWalk", 0L); Call("Update", 100u, true, false); }
        internal Array Published => (Array)Get("published");
        internal int SoundId => (int)Published.GetValue(0)!.GetType().GetProperty("SoundId")!.GetValue(Published.GetValue(0))!;
        internal Vector3 Position => (Vector3)Published.GetValue(0)!.GetType().GetProperty("Position")!.GetValue(Published.GetValue(0))!;
        internal object[] Scratch => new[] { "poolSnapshot", "candidates", "instances", "sources", "duplicateIds", "mapNodes", "mapTodo", "mapSeen" }.Select(Get).ToArray();
        internal bool TrySource(object instance, object candidate)
        { var args = new object?[] { (nint)Layout, instance, candidate, null }; return (bool)Call("TrySource", args)!; }
        internal void ProtectPool(bool readable)
        {
            if (!VirtualProtect((nint)(Pool + 0xC000), 4096, readable ? 4u : 1u, out _))
                throw new InvalidOperationException("Cannot change fixture pool readability");
        }
        public void Dispose()
        { foreach (var memory in buffers) NativeMemory.Free((void*)memory); VirtualFree((nint)Pool, 0, 0x8000); Special.Dispose(); }
    }

    [DllImport("kernel32.dll")] private static extern nint VirtualAlloc(nint address, nuint size, uint allocation, uint protection);
    [DllImport("kernel32.dll")] private static extern bool VirtualProtect(nint address, nuint size, uint protection, out uint previous);
    [DllImport("kernel32.dll")] private static extern bool VirtualFree(nint address, nuint size, uint freeType);
}
