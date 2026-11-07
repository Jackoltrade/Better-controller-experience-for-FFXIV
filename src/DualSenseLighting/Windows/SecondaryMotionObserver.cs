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

/// <summary>Read-only association of a native motion producer with its indexed position and rotation writes.</summary>
internal sealed class SecondaryMotionObserver : IDisposable
{
    internal const string ProducerSignature = "48 8B C4 48 89 70 20 57 48 81 EC A0 00 00 00 0F 29 70 E8 48 8B FA F3 0F 10 35 ?? ?? ?? ?? 4C 8B C1 48 89 58 18 48 8B 59 48 44 0F 29 50 A8 45 0F 57 D2 F6 43 10 04";
    internal const string SetterSignature = "48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 41 56 48 83 EC 20 48 8B F1 48 63 FA 48 81 C1 B0 00 00 00 4D 8B F0 FF 15 ?? ?? ?? ?? 41 8B 06 48 8D 4F 09";
    internal const string PositionSetterSignature = "48 89 5C 24 08 48 89 74 24 10 48 89 7C 24 18 41 56 48 83 EC 20 48 8B F1 48 63 FA 48 81 C1 B0 00 00 00 4D 8B F0 FF 15 ?? ?? ?? ?? 41 8B 06 48 8D 4F 07";
    internal const string ConstructorSignature = "48 83 EC 28 48 85 C9 75 18 33 D2 B9 98 00 00 00 44 8D 42 10 E8 ?? ?? ?? ?? 48 8B C8 48 85 C0 74 7F 33 C0 48 89 41 08 88 41 10 80 61 11 80 48 89 41 18";
    internal const string ResetSignature = "48 8B 51 48 48 85 D2 74 5C C7 41 74 01 00 00 00 C7 41 78 01 00 00 00 0F B7 42 02 66 0F 6E C0 0F 5B C0 F3 0F 58 41 58 F3 0F 11 41 50";
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ProducerDelegate(nint source, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void SetterDelegate(nint manager, int index, nint vector);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ResetDelegate(nint source);

    private readonly Hook<ProducerDelegate> producer;
    private readonly Hook<SetterDelegate> rotationSetter, positionSetter;
    private readonly Hook<ResetDelegate> reset;
    private readonly Func<SecondaryMotionSession?> current;
    private readonly nint sourceTable, managerGlobal;
    private int enabled, disposed, faulted;
    private string? error;
    public string? Error => Volatile.Read(ref error);
    private readonly record struct Scope(SecondaryMotionObserver Owner, SecondaryMotionSession Session,
        long Source, long Context, long Config);
    [ThreadStatic] private static Scope? scope;

    public unsafe SecondaryMotionObserver(IGameInteropProvider interop, ISigScanner scanner, Func<SecondaryMotionSession?> current)
    {
        this.current = current;
        var start = scanner.Module.BaseAddress;
        var size = scanner.Module.ModuleMemorySize;
        ReadOnlySpan<byte> Code(nint address, int length)
        {
            var original = GameAudioLayout.InspectionAddress(address, start, scanner.SearchBase, size, length);
            if (original == 0) throw new NotSupportedException("Secondary motion code is outside the game image.");
            return new ReadOnlySpan<byte>((void*)original, length);
        }
        nint Unique(string signature)
        {
            var matches = scanner.ScanAllText(signature);
            if (matches.Length != 1) throw new NotSupportedException("Secondary motion tracing is unsupported on this game build.");
            return matches[0];
        }
        var update = Unique(ProducerSignature);
        var write = Unique(SetterSignature);
        var positionWrite = Unique(PositionSetterSignature);
        var updateCode = Code(update, 0x263);
        var writeCode = Code(write, 0x6B);
        var positionCode = Code(positionWrite, 0x6B);
        if (!updateCode[0x171..0x17C].SequenceEqual(new byte[] { 0x8B, 0x43, 0x10, 0x48, 0x8B, 0x7F, 0x20, 0x0F, 0xB6, 0x73, 0x14 }) ||
            updateCode[0x245] != 0xE8 || update + 0x24A + BinaryPrimitives.ReadInt32LittleEndian(updateCode[0x246..]) != write ||
            !updateCode[0x1FB..0x1FE].SequenceEqual(new byte[] { 0x48, 0x8B, 0x0D }) ||
            !ValidSetter(writeCode, 0x09) || !ValidSetter(positionCode, 0x07) ||
            updateCode[0x1C6] != 0xE8 || update + 0x1CB + BinaryPrimitives.ReadInt32LittleEndian(updateCode[0x1C7..]) != positionWrite ||
            !updateCode[0x182..0x186].SequenceEqual(new byte[] { 0xA8, 0x01, 0x74, 0x48 }) ||
            !updateCode[0x186..0x189].SequenceEqual(new byte[] { 0x48, 0x8B, 0x0D }) ||
            !updateCode[0x18D..0x192].SequenceEqual(new byte[] { 0x4C, 0x8D, 0x44, 0x24, 0x20 }) ||
            !updateCode[0x1A5..0x1A7].SequenceEqual(new byte[] { 0x8B, 0xD6 }) ||
            positionWrite + 0x2B + BinaryPrimitives.ReadInt32LittleEndian(positionCode[0x27..]) !=
                write + 0x2B + BinaryPrimitives.ReadInt32LittleEndian(writeCode[0x27..]) ||
            positionWrite + 0x6B + BinaryPrimitives.ReadInt32LittleEndian(positionCode[0x67..]) !=
                write + 0x6B + BinaryPrimitives.ReadInt32LittleEndian(writeCode[0x67..]))
            throw new NotSupportedException("Secondary motion vector/call validation failed.");
        managerGlobal = update + 0x202 + BinaryPrimitives.ReadInt32LittleEndian(updateCode[0x1FE..]);
        if (update + 0x18D + BinaryPrimitives.ReadInt32LittleEndian(updateCode[0x189..]) != managerGlobal)
            throw new NotSupportedException("Secondary position manager validation failed.");
        Code(managerGlobal, 8);
        // Generic constructors have identical prefixes. Select the one whose
        // derived vtable dispatches D8 to this independently validated producer.
        var selected = 0;
        foreach (var candidate in scanner.ScanAllText(ConstructorSignature))
        {
            var creation = Code(candidate, 0xA0);
            if (!creation[0x85..0x88].SequenceEqual(new byte[] { 0x48, 0x8D, 0x05 })) continue;
            var table = candidate + 0x8C + BinaryPrimitives.ReadInt32LittleEndian(creation[0x88..]);
            if (GameAudioLayout.InspectionAddress(table, start, scanner.SearchBase, size, 0xE0) == 0) continue;
            if ((nint)BinaryPrimitives.ReadInt64LittleEndian(Code(table + 0xD8, 8)) != update) continue;
            sourceTable = table;
            selected++;
        }
        if (selected != 1) throw new NotSupportedException("Secondary motion source type is ambiguous.");
        var resetAddress = Unique(ResetSignature);
        var resetCode = Code(resetAddress, 0x66);
        var rebind = (nint)BinaryPrimitives.ReadInt64LittleEndian(Code(sourceTable + 0x68, 8));
        var rebindCode = Code(rebind, 0x3D);
        if (rebindCode[0x23] != 0xE8 || rebind + 0x28 + BinaryPrimitives.ReadInt32LittleEndian(rebindCode[0x24..]) != resetAddress ||
            !resetCode[0x35..0x3C].SequenceEqual(new byte[] { 0xC7, 0x41, 0x6C, 0, 0, 0x80, 0xBF }) ||
            !resetCode[0x40..0x4F].SequenceEqual(new byte[] { 0x8B, 0x81, 0x8C, 0, 0, 0, 0x89, 0x91, 0x8C, 0, 0, 0, 0x0F, 0x5B, 0xC0 }) ||
            resetCode[0x65] != 0xC3)
            throw new NotSupportedException("Secondary motion reset validation failed.");
        producer = interop.HookFromAddress<ProducerDelegate>(update, Produce);
        Hook<SetterDelegate>? createdRotation = null, createdPosition = null;
        try
        {
            rotationSetter = createdRotation = interop.HookFromAddress<SetterDelegate>(write, SetRotation);
            positionSetter = createdPosition = interop.HookFromAddress<SetterDelegate>(positionWrite, SetPosition);
            reset = interop.HookFromAddress<ResetDelegate>(resetAddress, ResetSource);
        }
        catch (Exception failure)
        {
            try { ResourceCleanup.Run(() => createdPosition?.Dispose(), () => createdRotation?.Dispose(), producer.Dispose); }
            catch (Exception cleanup) { throw new AggregateException("Motion tracing acquisition and rollback failed.", failure, cleanup); }
            throw;
        }
    }

    private static bool ValidSetter(ReadOnlySpan<byte> code, byte vectorBase) =>
        code[0x18..0x25].SequenceEqual(new byte[] { 0x48, 0x63, 0xFA, 0x48, 0x81, 0xC1, 0xB0, 0, 0, 0, 0x4D, 0x8B, 0xF0 }) &&
        code[0x2B..0x48].SequenceEqual(new byte[] { 0x41, 0x8B, 0x06, 0x48, 0x8D, 0x4F, vectorBase,
            0x48, 0x03, 0xC9, 0x89, 0x04, 0xCE, 0x41, 0x8B, 0x46, 0x04, 0x89, 0x44, 0xCE, 0x04,
            0x41, 0x8B, 0x46, 0x08, 0x89, 0x44, 0xCE, 0x08 }) &&
        code[0x48..0x4F].SequenceEqual(new byte[] { 0x48, 0x8D, 0x8E, 0xB0, 0, 0, 0 }) &&
        code[0x64..0x67].SequenceEqual(new byte[] { 0x48, 0xFF, 0x25 });

    public void SetActive(bool active)
    {
        active &= Volatile.Read(ref disposed) == 0 && Volatile.Read(ref faulted) == 0;
        if (active && Volatile.Read(ref enabled) != 0) return;
        if (!active && Volatile.Read(ref enabled) == 0 && !producer.IsEnabled && !rotationSetter.IsEnabled &&
            !positionSetter.IsEnabled && !reset.IsEnabled) return;
        Volatile.Write(ref enabled, 0);
        try
        {
            if (active)
            {
                reset.Enable(); positionSetter.Enable(); rotationSetter.Enable(); producer.Enable();
                Volatile.Write(ref enabled, 1);
            }
            else ResourceCleanup.Run(() => { if (producer.IsEnabled) producer.Disable(); },
                () => { if (rotationSetter.IsEnabled) rotationSetter.Disable(); },
                () => { if (positionSetter.IsEnabled) positionSetter.Disable(); }, () => { if (reset.IsEnabled) reset.Disable(); });
        }
        catch (Exception failure)
        {
            Fault(failure);
            try { if (producer.IsEnabled) producer.Disable(); }
            catch (Exception cleanup) { Volatile.Write(ref error, failure.Message + "; " + cleanup.Message); }
            try { if (rotationSetter.IsEnabled) rotationSetter.Disable(); }
            catch (Exception cleanup) { Volatile.Write(ref error, failure.Message + "; " + cleanup.Message); }
            try { if (positionSetter.IsEnabled) positionSetter.Disable(); }
            catch (Exception cleanup) { Volatile.Write(ref error, failure.Message + "; " + cleanup.Message); }
            try { if (reset.IsEnabled) reset.Disable(); }
            catch (Exception cleanup) { Volatile.Write(ref error, failure.Message + "; " + cleanup.Message); }
        }
    }

    private SecondaryMotionSession? Current() => Volatile.Read(ref enabled) != 0 && Volatile.Read(ref disposed) == 0 ? current() : null;

    private void Produce(nint source, nint context)
    {
        var previous = scope;
        scope = null;
        try
        {
            try
            {
                var recording = Current();
                Span<byte> table = stackalloc byte[8];
                Span<byte> config = stackalloc byte[8];
                if (recording != null && GameScreenEffects.Copy(source, table) && Pointer(table) == sourceTable &&
                    source <= nint.MaxValue - 0x50 && GameScreenEffects.Copy(source + 0x48, config))
                    scope = new(this, recording, source, context, Pointer(config));
            }
            catch (Exception failure) { Fault(failure); }
            producer.OriginalDisposeSafe(source, context);
        }
        finally { scope = previous; }
    }

    private void SetRotation(nint manager, int index, nint vector)
    {
        rotationSetter.OriginalDisposeSafe(manager, index, vector);
        ObserveVector(manager, index, vector, position: false);
    }

    private void SetPosition(nint manager, int index, nint vector)
    {
        positionSetter.OriginalDisposeSafe(manager, index, vector);
        ObserveVector(manager, index, vector, position: true);
    }

    private void ObserveVector(nint manager, int index, nint vector, bool position)
    {
        try
        {
            var recording = Current();
            if (recording == null) return;
            Span<byte> owner = stackalloc byte[8];
            Span<byte> values = stackalloc byte[12];
            if (!GameScreenEffects.Copy(managerGlobal, owner) || manager != Pointer(owner) || !GameScreenEffects.Copy(vector, values))
            { recording.Gap(Stopwatch.GetTimestamp()); return; }
            var active = scope;
            var scoped = active.HasValue && ReferenceEquals(active.Value.Owner, this) && ReferenceEquals(active.Value.Session, recording);
            var sample = new SecondaryVectorSample(scoped ? active!.Value.Source : 0, scoped ? active!.Value.Context : 0,
                scoped ? active!.Value.Config : 0, index, scoped,
                BinaryPrimitives.ReadSingleLittleEndian(values), BinaryPrimitives.ReadSingleLittleEndian(values[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(values[8..]));
            var clock = Stopwatch.GetTimestamp();
            recording.Observe(sample, position, clock);
        }
        catch (Exception failure) { Fault(failure); }
    }

    private void ResetSource(nint source)
    {
        try
        {
            var recording = Current();
            Span<byte> table = stackalloc byte[8];
            if (recording != null && GameScreenEffects.Copy(source, table) && Pointer(table) == sourceTable && source <= nint.MaxValue - 0x90)
            {
                Span<byte> context = stackalloc byte[8];
                Span<byte> config = stackalloc byte[8];
                Span<byte> cursor = stackalloc byte[4];
                Span<byte> state = stackalloc byte[4];
                if (GameScreenEffects.Copy(source + 0x18, context) && GameScreenEffects.Copy(source + 0x48, config) &&
                    GameScreenEffects.Copy(source + 0x6C, cursor) && GameScreenEffects.Copy(source + 0x8C, state))
                    recording.Reset(source, Pointer(context), Pointer(config), BinaryPrimitives.ReadUInt32LittleEndian(cursor),
                        BinaryPrimitives.ReadUInt32LittleEndian(state), Stopwatch.GetTimestamp());
                else recording.Gap(Stopwatch.GetTimestamp());
            }
        }
        catch (Exception failure) { Fault(failure); }
        reset.OriginalDisposeSafe(source);
    }

    private static nint Pointer(ReadOnlySpan<byte> bytes) => (nint)BinaryPrimitives.ReadInt64LittleEndian(bytes);
    private void Fault(Exception failure)
    {
        Volatile.Write(ref faulted, 1); Volatile.Write(ref enabled, 0);
        Interlocked.CompareExchange(ref error, failure.Message, null);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref enabled, 0);
        ResourceCleanup.Run(reset.Dispose, rotationSetter.Dispose, positionSetter.Dispose, producer.Dispose);
    }
}
