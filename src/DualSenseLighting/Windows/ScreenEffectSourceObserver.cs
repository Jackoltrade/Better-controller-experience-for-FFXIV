// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>Opt-in research metadata, before AVFX quake candidates lose their identity in the shared accumulator.</summary>
internal sealed class ScreenEffectSourceObserver : IDisposable
{
    internal const string ConstructorSignature = "48 89 5C 24 08 57 48 83 EC 40 F3 0F 10 84 24 80 00 00 00 48 8B F9 F3 0F 10 4C 24 78 48 8B 44 24 70 F3 0F 11 44 24 30 F3 0F 11 4C 24 28 48 89 44 24 20 E8 ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 07 48 8B 87 28 02 00 00 48 8B 48 10 48 83 C1 08 E8 ?? ?? ?? ?? 88 87 30 02 00 00";
    internal const string SampleSignature = "4C 8B DC 57 48 81 EC B0 00 00 00 48 8B 81 F8 01 00 00 48 8B F9 45 0F 29 4B B8 44 0F 28 CA";

    // Both allocated factories and the constructor's nested sample call were
    // checked in the installed 2026.10.06 executable. Floats 6/7 are stack args.
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint ConstructorDelegate(nint self, nint owner, nint parent, nint inheritedResource,
        nint authoredWrapper, float arg6, float arg7);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void SampleDelegate(nint self, float arg1, float arg2);

    private readonly Hook<ConstructorDelegate> constructor;
    private readonly Hook<SampleDelegate> sampler;
    private readonly nint sourceTable;
    private ScreenEffectSourceRecording? recording;
    private int enabled, disposed, faulted;
    private string? error;
    public string? Error => Volatile.Read(ref error);

    public unsafe ScreenEffectSourceObserver(IGameInteropProvider interop, ISigScanner scanner)
    {
        var moduleStart = scanner.Module.BaseAddress;
        var moduleEnd = checked(moduleStart + scanner.Module.ModuleMemorySize);
        nint Unique(string signature)
        {
            var matches = scanner.ScanAllText(signature);
            if (matches.Length != 1) throw new NotSupportedException("Individual quake-source tracing is unsupported on this game build.");
            return matches[0];
        }
        ReadOnlySpan<byte> Code(nint address, int size)
        {
            var inspection = GameAudioLayout.InspectionAddress(address, moduleStart, scanner.SearchBase,
                scanner.Module.ModuleMemorySize, size);
            if (inspection == 0) throw new NotSupportedException("Quake-source code validation failed.");
            return new ReadOnlySpan<byte>((void*)inspection, size);
        }
        var create = Unique(ConstructorSignature);
        var sample = Unique(SampleSignature);
        var creation = Code(create, 0x12E);
        sourceTable = create + 0x3E + BinaryPrimitives.ReadInt32LittleEndian(creation[0x3A..]);
        var body = Code(sample, 0x356);
        if (sourceTable < moduleStart || sourceTable > moduleEnd - 0x100 ||
            creation[0x11B] != 0xE8 || create + 0x120 + BinaryPrimitives.ReadInt32LittleEndian(creation[0x11C..]) != sample ||
            !creation[0x113..0x11B].SequenceEqual(new byte[] { 0xF3, 0x0F, 0x10, 0x8F, 0xC8, 1, 0, 0 }) ||
            body.IndexOf(new byte[] { 0x48, 0x8B, 0x87, 0x28, 2, 0, 0 }) < 0 ||
            body.IndexOf(new byte[] { 0x80, 0x7B, 4, 6 }) < 0 ||
            body.IndexOf(new byte[] { 0x48, 0x8B, 0x88, 0x90, 0x14, 0, 0 }) < 0 ||
            body.IndexOf(new byte[] { 0x0F, 0x2F, 0xB1, 0xC0, 1, 0, 0 }) < 0 ||
            body.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x89, 0xD8, 1, 0, 0 }) < 0)
            throw new NotSupportedException("Quake-source parameter validation failed.");
        var adapter = (nint)BinaryPrimitives.ReadInt64LittleEndian(Code(sourceTable + 0xF8, 8));
        var adapterCode = Code(adapter, 0x15);
        if (!adapterCode[..16].SequenceEqual(new byte[] { 0xF3, 0x0F, 0x10, 0x91, 0xCC, 1, 0, 0,
                0xF3, 0x0F, 0x10, 0x89, 0xC8, 1, 0, 0 }) || adapterCode[16] != 0xE9 ||
            adapter + 0x15 + BinaryPrimitives.ReadInt32LittleEndian(adapterCode[17..]) != sample)
            throw new NotSupportedException("Quake-source virtual adapter validation failed.");

        constructor = interop.HookFromAddress<ConstructorDelegate>(create, Construct);
        try { sampler = interop.HookFromAddress<SampleDelegate>(sample, Sample); }
        catch (Exception failure)
        {
            try { constructor.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Quake-source tracing initialization and rollback failed.", failure, cleanup); }
            throw;
        }
    }

    public ScreenEffectSourceRecording Start(long clock)
    {
        var next = new ScreenEffectSourceRecording(clock);
        Interlocked.Exchange(ref recording, next)?.Retire();
        return next;
    }

    public void Stop()
    {
        Interlocked.Exchange(ref recording, null)?.Retire();
        if (Volatile.Read(ref disposed) == 0) SetActive(false);
    }

    // Called only on the framework thread. Additional hooks remain disabled in
    // normal play and when a consented recording is paused by the focus guards.
    public void SetActive(bool active)
    {
        active &= Volatile.Read(ref disposed) == 0 && Volatile.Read(ref faulted) == 0 && Volatile.Read(ref recording) != null;
        if (active && Volatile.Read(ref enabled) != 0) return;
        if (!active && Volatile.Read(ref enabled) == 0 && !constructor.IsEnabled && !sampler.IsEnabled) return;
        Volatile.Write(ref enabled, 0);
        Volatile.Read(ref recording)?.OnGap(Stopwatch.GetTimestamp());
        try
        {
            if (active)
            {
                constructor.Enable();
                sampler.Enable();
                Volatile.Write(ref enabled, 1);
            }
            else
            {
                try { if (sampler.IsEnabled) sampler.Disable(); }
                finally { if (constructor.IsEnabled) constructor.Disable(); }
            }
        }
        catch (Exception failure)
        {
            Fault(failure);
            // Retry any unsuccessful cleanup on the next framework update or
            // disposal; a diagnostic failure must not stop ordinary haptics.
            try { if (sampler.IsEnabled) sampler.Disable(); }
            catch (Exception cleanup) { Volatile.Write(ref error, failure.Message + "; " + cleanup.Message); }
            try { if (constructor.IsEnabled) constructor.Disable(); }
            catch (Exception cleanup) { Volatile.Write(ref error, failure.Message + "; " + cleanup.Message); }
        }
    }

    private ScreenEffectSourceRecording? Current()
    {
        if (Volatile.Read(ref enabled) == 0 || Volatile.Read(ref disposed) != 0) return null;
        var current = Volatile.Read(ref recording);
        if (Native.IsCurrentProcessForeground()) return current;
        // A brief focus change may fall between framework updates. Missing a
        // construction in that gap must invalidate the old address's identity.
        current?.OnGap(Stopwatch.GetTimestamp());
        return null;
    }

    private nint Construct(nint self, nint owner, nint parent, nint inheritedResource, nint authoredWrapper, float arg6, float arg7)
    {
        // Register entry before Original: it samples the new node before its
        // constructor returns. Never dereference uninitialized storage here.
        try { Current()?.OnConstruction(self, Stopwatch.GetTimestamp()); }
        catch (Exception failure) { Fault(failure); }
        return constructor.OriginalDisposeSafe(self, owner, parent, inheritedResource, authoredWrapper, arg6, arg7);
    }

    private void Sample(nint self, float arg1, float arg2)
    {
        try
        {
            var current = Current();
            if (current != null && Read(self, arg1, arg2, out var value)) current.OnSample(value, Stopwatch.GetTimestamp());
            else if (current != null) current.OnGap(Stopwatch.GetTimestamp());
        }
        catch (Exception failure) { Fault(failure); }
        sampler.OriginalDisposeSafe(self, arg1, arg2);
    }

    private bool Read(nint self, float arg1, float arg2, out ScreenEffectSourceSample sample)
    {
        sample = default;
        Span<byte> table = stackalloc byte[8];
        if (!GameScreenEffects.Copy(self, table) || Pointer(table, 0) != sourceTable) return false;
        // The two validated factory paths allocate 0x240 bytes for types 6/9.
        // Only the named scalar fields below are retained; no raw dump is saved.
        Span<byte> node = stackalloc byte[0x240];
        if (!GameScreenEffects.Copy(self, node) || Pointer(node, 0) != sourceTable) return false;
        var wrapper = Pointer(node, 0x228);
        Span<byte> authored = stackalloc byte[0x18];
        Span<byte> identity = stackalloc byte[0x38];
        if (!GameScreenEffects.Copy(wrapper, authored) || authored[4] is not (6 or 9) ||
            !GameScreenEffects.Copy(self + 0x1F8, identity) || Pointer(identity, 0) != Pointer(node, 0x1F8) ||
            Pointer(identity, 0x30) != wrapper || !GameScreenEffects.Copy(self, table) || Pointer(table, 0) != sourceTable)
            return false;
        sample = new(self, Pointer(node, 0x1F8), Pointer(node, 0x200), Pointer(node, 0x208), wrapper,
            Pointer(authored, 0x10), authored[4], arg1, arg2,
            BinaryPrimitives.ReadSingleLittleEndian(node[0x1C8..]), BinaryPrimitives.ReadSingleLittleEndian(node[0x1CC..]),
            BinaryPrimitives.ReadSingleLittleEndian(node[0x1D0..]), BinaryPrimitives.ReadSingleLittleEndian(node[0x1D4..]));
        return true;
    }

    private static nint Pointer(ReadOnlySpan<byte> bytes, int offset) => (nint)BinaryPrimitives.ReadInt64LittleEndian(bytes[offset..]);
    private void Fault(Exception failure)
    {
        Volatile.Write(ref faulted, 1);
        Volatile.Write(ref enabled, 0);
        Interlocked.CompareExchange(ref error, failure.Message, null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref enabled, 0);
        Interlocked.Exchange(ref recording, null)?.Retire();
        ResourceCleanup.Run(sampler.Dispose, constructor.Dispose);
    }
}
