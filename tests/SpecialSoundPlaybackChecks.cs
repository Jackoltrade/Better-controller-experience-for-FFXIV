// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

internal static unsafe class SpecialSoundPlaybackChecks
{
    private delegate bool Resolve(nint soundData, out int playbackId);
    private const int PlaybackId = 492626514, StaleUiId = 481935372, RequestedId = 4043;

    internal static void Run(Action<bool, string> check, Assembly stagedPlugin)
    {
        if (!OperatingSystem.IsWindows()) return;
        var type = stagedPlugin.GetType("DualSenseLighting.Windows.SpecialSoundPlayback", throwOnError: true)!;
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(nint), typeof(nint), typeof(nint) }, null) ?? throw new MissingMethodException(type.FullName, ".ctor");
        var method = type.GetMethod("TryResolve") ?? throw new MissingMethodException(type.FullName, "TryResolve");
        var error = type.GetProperty("Error") ?? throw new MissingMemberException(type.FullName, "Error");
        using var fixture = new Fixture();
        var resolver = Create(constructor, method, fixture.Global, fixture.Module, fixture.Module + Fixture.ModuleSize);
        check(error.GetValue(resolver.Instance) == null, "The pinned unmanaged special-playback fixture failed startup validation");
        CheckExactJoin(check, fixture, resolver.Call);
        CheckDeferredAndInvalid(check, fixture, resolver.Call);
        CheckListIntegrity(check, fixture, resolver.Call);
        CheckBounds(check, fixture, resolver.Call);
        CheckLifetimeChanges(check, fixture, resolver.Call);
        CheckValidationFailures(check, fixture, constructor, method, error);
        CheckHotPath(check, fixture, resolver.Call);
    }

    private static (object Instance, Resolve Call) Create(ConstructorInfo constructor, MethodInfo method,
        byte* global, byte* moduleStart, byte* moduleEnd)
    {
        var instance = constructor.Invoke(new object[] { (nint)global, (nint)moduleStart, (nint)moduleEnd });
        return (instance, method.CreateDelegate<Resolve>(instance));
    }

    private static void Reject(Action<bool, string> check, Resolve resolve, byte* sound, string message)
    {
        var result = resolve((nint)sound, out var id);
        check(!result && id == 0, message);
    }

    private static void CheckExactJoin(Action<bool, string> check, Fixture f, Resolve resolve)
    {
        f.Reset(); var before = new ReadOnlySpan<byte>(f.Sound, 0xD0).ToArray();
        check(resolve((nint)f.Sound, out var id) && id == PlaybackId && id != StaleUiId,
            "A special sound used its stale embedded UI controller instead of the exact system/layout entry controller");
        foreach (var stale in new[] { 0, -2145114664, StaleUiId, int.MaxValue })
        {
            *(int*)(f.Sound + 0x40) = stale;
            check(resolve((nint)f.Sound, out id) && id == PlaybackId,
                "The special backend incorrectly depended on an unused embedded controller ID");
        }
        *(int*)(f.Sound + 0x40) = StaleUiId;
        check(before.AsSpan().SequenceEqual(new ReadOnlySpan<byte>(f.Sound, 0xD0)),
            "Resolving special playback modified the native SoundData fixture");
        for (var list = 1; list < 3; list++)
        {
            f.Reset(); new Span<byte>(f.List(0), 0x20).Clear(); f.SetList(list, 0, 1, 0x20);
            check(resolve((nint)f.Sound, out id) && id == PlaybackId,
                "A valid sound entry in a secondary native list was missed");
        }
        f.Reset(); *(int*)(f.Sound + 0x98) = 0; *(int*)(f.Entry(0) + 0x38) = 0;
        check(resolve((nint)f.Sound, out id) && id == PlaybackId,
            "A valid zero-valued native entry identifier was mistaken for missing data");
    }

    private static void CheckDeferredAndInvalid(Action<bool, string> check, Fixture f, Resolve resolve)
    {
        var mutations = new Action<Fixture>[] {
            x => x.Sound[0x24] = 0, x => x.Sound[0x24] = 2,
            x => *(nint*)(x.Sound + 0x28) = 0, x => *(int*)(x.Sound + 0xB0) = 0,
            x => *(int*)(x.Sound + 0xB0) = 2, x => *(int*)(x.Sound + 0x98) = -1,
            x => *(nint*)(x.Sound + 0x90) = 0, x => *(int*)(x.Manager + 8) = 0,
            x => *(int*)(x.Manager + 8) = -1, x => *(int*)(x.Manager + 8) = 2,
            x => *(int*)(x.Entry(0) + 0x38) = RequestedId + 1,
            x => *(int*)(x.Entry(0) + 0x10) = 0, x => *(int*)(x.Entry(0) + 0x10) = -123,
            x => *(nint*)(x.Entry(0) + 0x18) = 0,
            x => *(nint*)(x.Entry(0) + 0x18) = (nint)x.Manager,
            x => *(nint*)(x.Entry(0) + 0x30) = (nint)x.Layout(0),
            x => *(nint*)x.Engine = (nint)x.Engine,
        };
        for (var i = 0; i < mutations.Length; i++)
        {
            f.Reset(); mutations[i](f);
            Reject(check, resolve, f.Sound, "Deferred, ordinary, or mismatched special playback produced a guessed ID (case " + i + ")");
        }
        f.Reset(); Reject(check, resolve, null, "A null SoundData pointer produced playback");
        Reject(check, resolve, (byte*)nint.MaxValue, "An overflowing SoundData read produced playback");
        f.Reset(); *(int*)(f.Entry(0) + 0x10) = 0;
        Reject(check, resolve, f.Sound, "A controller that had not started yet was treated as audible playback");
        *(int*)(f.Entry(0) + 0x10) = PlaybackId;
        check(resolve((nint)f.Sound, out var id) && id == PlaybackId,
            "An initially deferred entry never became resolvable after native initialization");
    }

    private static void CheckListIntegrity(Action<bool, string> check, Fixture f, Resolve resolve)
    {
        var mutations = new Action<Fixture>[] {
            x => *(int*)(x.List(0) + 0x10) = -1,
            x => *(int*)(x.List(0) + 0x10) = 513,
            x => *(nint*)(x.List(0) + 0x18) = -8,
            x => *(nint*)(x.List(0) + 0x18) = 3,
            x => *(nint*)(x.List(0) + 0x18) = 264,
            x => *(int*)(x.List(0) + 0x10) = 0,
            x => *(int*)(x.List(0) + 0x10) = 2,
            x => *(nint*)(x.List(0) + 8) = (nint)x.Node(1),
            x => *(nint*)x.Node(0) = 0,
            x => *(nint*)(x.Node(0) + 0x28) = (nint)x.Node(0),
        };
        for (var i = 0; i < mutations.Length; i++)
        {
            f.Reset(); mutations[i](f);
            Reject(check, resolve, f.Sound, "A corrupt, cyclic, truncated or overlong native list produced playback (case " + i + ")");
        }
        f.Reset(); f.InitializeEntry(1, RequestedId); f.SetList(1, 1, 1, 0x20);
        Reject(check, resolve, f.Sound, "Two native entries sharing the requested ID were resolved by arbitrary list order");
        f.Reset(); *(int*)(f.Engine + 0x68) = 3; *(int*)(f.Layout(2) + 0x18) = 1;
        Reject(check, resolve, f.Sound, "Duplicate matching system layouts were resolved by arbitrary array order");
        f.Reset(); *(nint*)(f.List(0) + 0x18) = 256;
        check(resolve((nint)f.Sound, out var id) && id == PlaybackId,
            "The largest supported aligned node linkage displacement was rejected");
    }

    private static void CheckBounds(Action<bool, string> check, Fixture f, Resolve resolve)
    {
        foreach (var count in new[] { -1, 0, 129, int.MaxValue })
        {
            f.Reset(); *(int*)(f.Engine + 0x68) = count;
            Reject(check, resolve, f.Sound, "An invalid engine layout count escaped bounded traversal");
        }
        f.Reset(); *(int*)(f.Engine + 0x68) = 128;
        check(resolve((nint)f.Sound, out var id) && id == PlaybackId,
            "A valid engine with the supported maximum number of layouts was rejected");
        f.Reset();
        for (var i = 0; i < Fixture.EntryCount; i++)
        {
            f.InitializeEntry(i, i == Fixture.EntryCount - 1 ? RequestedId : 10000 + i);
            *(nint*)(f.Node(i) + 0x28) = i == Fixture.EntryCount - 1 ? 0 : (nint)f.Node(i + 1);
        }
        f.SetList(0, 0, Fixture.EntryCount, 0x20);
        check(resolve((nint)f.Sound, out id) && id == PlaybackId,
            "A valid entry at the final supported native list position was missed");
    }

    private static void CheckLifetimeChanges(Action<bool, string> check, Fixture f, Resolve resolve)
    {
        f.Reset(); check(resolve((nint)f.Sound, out _), "Lifecycle fixture did not start with valid playback");
        *(nint*)f.Global = (nint)f.OtherEngine;
        Reject(check, resolve, f.Sound, "Replacing the live engine retained an earlier engine's layout mapping");
        *(nint*)f.Global = (nint)f.Engine;
        *(int*)(f.Layout(1) + 0x18) = 2;
        Reject(check, resolve, f.Sound, "A reused layout retained the previous system identity");
        f.Reset(); *(int*)(f.Sound + 0x98) = RequestedId + 1;
        Reject(check, resolve, f.Sound, "A reused SoundData slot retained its previous entry identifier");
        f.Reset(); *(int*)(f.Entry(0) + 0x38) = RequestedId + 1;
        Reject(check, resolve, f.Sound, "A reused native entry retained its previous reservation identity");

        var unreadable = VirtualAlloc(0, 4096, 0x3000, 0x01);
        if (unreadable == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            f.Reset(); Reject(check, resolve, (byte*)unreadable, "Unreadable SoundData escaped RPM failure isolation");
            *(nint*)(f.Sound + 0x90) = unreadable;
            Reject(check, resolve, f.Sound, "An unreadable system manager produced a fabricated playback ID");
            f.Reset(); *(nint*)(f.Engine + 0xA8) = unreadable;
            Reject(check, resolve, f.Sound, "An unreadable engine array produced a fabricated playback ID");
            f.Reset(); *(nint*)f.Node(0) = unreadable;
            Reject(check, resolve, f.Sound, "An unreadable native entry produced a fabricated playback ID");
        }
        finally { if (!VirtualFree(unreadable, 0, 0x8000)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        f.Reset();
        check(resolve((nint)f.Sound, out var id) && id == PlaybackId,
            "A safe read failure permanently disabled later valid playback resolution");
    }

    private static void CheckValidationFailures(Action<bool, string> check, Fixture f, ConstructorInfo constructor,
        MethodInfo method, PropertyInfo error)
    {
        foreach (var offset in new[] { 0x2D, 0x3D, 0x4C })
        {
            f.Reset(); f.Lookup[offset]++;
            var invalid = Create(constructor, method, f.Global, f.Module, f.Module + Fixture.ModuleSize);
            check(error.GetValue(invalid.Instance) is string,
                "Changed native engine lookup fields passed startup layout validation");
            Reject(check, invalid.Call, (byte*)nint.MaxValue, "An unsupported layout attempted to resolve native playback");
        }
        f.Reset(); var outside = Create(constructor, method, f.Module + Fixture.ModuleSize, f.Module, f.Module + Fixture.ModuleSize);
        check(error.GetValue(outside.Instance) is string, "An engine-global address outside the validated module was trusted");
        f.Reset(); *(nint*)f.Engine = (nint)f.Engine;
        var badTable = Create(constructor, method, f.Global, f.Module, f.Module + Fixture.ModuleSize);
        check(error.GetValue(badTable.Instance) is string, "A heap pointer was accepted as the pinned engine vtable");
        f.Reset(); *(nint*)(f.Table + 0x140) = (nint)(f.Module + Fixture.ModuleSize - 1);
        var shortBody = Create(constructor, method, f.Global, f.Module, f.Module + Fixture.ModuleSize);
        check(error.GetValue(shortBody.Instance) is string, "A truncated native lookup body passed module-bound validation");
    }

    private static void CheckHotPath(Action<bool, string> check, Fixture f, Resolve resolve)
    {
        f.Reset();
        for (var i = 0; i < 2000; i++) resolve((nint)f.Sound, out _);
        var mapped = 0;
        Action resolvePlayback = () =>
        {
            if (resolve((nint)f.Sound, out var id) && id == PlaybackId) mapped++;
        };
        var allocated = AllocationChecks.MeasureAllocations(resolvePlayback, warmup: 0, iterations: 10000);
        check(allocated == 0 && mapped == 10000,
            $"Repeated exact special-playback resolution allocated {allocated} bytes or lost a stable native mapping");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(nint address, nuint size, uint freeType);

    private sealed class Fixture : IDisposable
    {
        internal const int ModuleSize = 4096, EntryCount = 512;
        internal readonly byte* Module = (byte*)NativeMemory.AllocZeroed(ModuleSize);
        internal readonly byte* Engine = (byte*)NativeMemory.AllocZeroed(0xB0);
        internal readonly byte* OtherEngine = (byte*)NativeMemory.AllocZeroed(0xB0);
        internal readonly byte* Manager = (byte*)NativeMemory.AllocZeroed(16);
        internal readonly byte* Sound = (byte*)NativeMemory.AllocZeroed(0xD0);
        private readonly byte* layouts = (byte*)NativeMemory.AllocZeroed(128 * 0xB0);
        private readonly byte* array = (byte*)NativeMemory.AllocZeroed(128 * 8);
        private readonly byte* otherArray = (byte*)NativeMemory.AllocZeroed(8);
        private readonly byte* nodes = (byte*)NativeMemory.AllocZeroed(EntryCount * 0x110);
        private readonly byte* entries = (byte*)NativeMemory.AllocZeroed(EntryCount * 0x40);
        internal byte* Global => Module + 0x20;
        internal byte* Table => Module + 0x100;
        internal byte* Lookup => Module + 0x400;
        internal byte* Layout(int index) => layouts + index * 0xB0;
        internal byte* List(int index) => Layout(1) + 0x48 + index * 0x20;
        internal byte* Node(int index) => nodes + index * 0x110;
        internal byte* Entry(int index) => entries + index * 0x40;
        internal Fixture() => Reset();
        internal void Reset()
        {
            new Span<byte>(Module, ModuleSize).Clear(); new Span<byte>(Engine, 0xB0).Clear();
            new Span<byte>(OtherEngine, 0xB0).Clear(); new Span<byte>(Sound, 0xD0).Clear();
            new Span<byte>(layouts, 128 * 0xB0).Clear(); new Span<byte>(nodes, EntryCount * 0x110).Clear();
            new Span<byte>(entries, EntryCount * 0x40).Clear();
            *(nint*)Global = (nint)Engine; *(nint*)Engine = *(nint*)OtherEngine = (nint)Table;
            *(nint*)(Table + 0x140) = (nint)Lookup;
            new byte[] { 0x48, 0x63, 0x45, 0x68 }.CopyTo(new Span<byte>(Lookup + 0x2D, 4));
            new byte[] { 0x4C, 0x8B, 0x8D, 0xA8, 0, 0, 0 }.CopyTo(new Span<byte>(Lookup + 0x3A, 7));
            new byte[] { 0x39, 0x70, 0x18 }.CopyTo(new Span<byte>(Lookup + 0x4A, 3));
            for (var i = 0; i < 128; i++)
            { *(nint*)(array + i * 8) = (nint)Layout(i); *(int*)(Layout(i) + 0x18) = i; }
            *(int*)(Engine + 0x68) = 2; *(nint*)(Engine + 0xA8) = (nint)array;
            *(nint*)otherArray = (nint)Layout(2);
            *(int*)(OtherEngine + 0x68) = 1; *(nint*)(OtherEngine + 0xA8) = (nint)otherArray;
            *(int*)(Manager + 8) = 1;
            Sound[0x24] = 1; *(nint*)(Sound + 0x28) = 1; *(int*)(Sound + 0x40) = StaleUiId;
            *(nint*)(Sound + 0x90) = (nint)Manager; *(int*)(Sound + 0x98) = RequestedId; *(int*)(Sound + 0xB0) = 1;
            InitializeEntry(0, RequestedId); SetList(0, 0, 1, 0x20);
        }
        internal void InitializeEntry(int index, int requested)
        {
            *(nint*)Node(index) = (nint)Entry(index);
            *(int*)(Entry(index) + 0x10) = PlaybackId; *(nint*)(Entry(index) + 0x18) = (nint)Layout(0);
            *(nint*)(Entry(index) + 0x30) = (nint)Layout(1); *(int*)(Entry(index) + 0x38) = requested;
        }
        internal void SetList(int list, int first, int count, int displacement)
        {
            *(nint*)List(list) = (nint)Node(first); *(nint*)(List(list) + 8) = (nint)Node(first + count - 1);
            *(int*)(List(list) + 0x10) = count; *(nint*)(List(list) + 0x18) = displacement;
        }
        public void Dispose()
        {
            NativeMemory.Free(entries); NativeMemory.Free(nodes); NativeMemory.Free(otherArray); NativeMemory.Free(array);
            NativeMemory.Free(layouts); NativeMemory.Free(Sound); NativeMemory.Free(Manager);
            NativeMemory.Free(OtherEngine); NativeMemory.Free(Engine); NativeMemory.Free(Module);
        }
    }
}
