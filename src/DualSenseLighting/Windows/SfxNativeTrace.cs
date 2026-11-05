// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>Bounded, read-only native evidence for otherwise unattributed sounds.</summary>
internal sealed unsafe class SfxNativeTrace
{
    private const int AcquisitionCapacity = 128, VoiceCapacity = 64, PoolCapacity = 128, ResolutionCapacity = 128;
    private const int StackCapacity = 24, SoundSize = 0xD0;
    private readonly nint moduleStart, moduleEnd;
    private readonly GameAudioLayout.SoundData* pool;
    private Session? session;

    public sealed record Acquisition(double Seconds, int Slot, uint Generation, int ReservationId, string SlotAddress,
        byte Origin, string ScopedActor, uint ScopedEntity, EnemyActionTag ScopedAction, ImmutableArray<uint> StackRvas);
    public sealed record Voice(double Seconds, int SoundId, byte Bus, uint Flags,
        string Source, string Callback, string CallbackOwner, string Track, string Root,
        ImmutableArray<byte> RootHeader, ImmutableArray<byte> TrackHeader);
    public sealed record PoolSound(double Seconds, int Slot, uint Generation, int ReservationId, string SlotAddress,
        byte IsActive, string ResourceHandle, uint SoundNumber, ImmutableArray<byte> Header,
        ImmutableArray<byte> FirstActiveHeader, double? FirstActiveSeconds);
    public sealed record Resolution(double Seconds, string Layout, int ReservationId, string Entry,
        int ControllerId, string ControllerLayout, ImmutableArray<byte> Header);
    private readonly record struct ResolutionKey(nint Layout, int ReservationId, int ControllerId, nint ControllerLayout);

    private sealed class Session
    {
        public readonly long Started = Stopwatch.GetTimestamp();
        public int Active = 1, AcquisitionCount, PoolCount, VoiceGate, ResolutionCount, ResolutionGate;
        public string? Error;
        public readonly Acquisition?[] Acquisitions = new Acquisition?[AcquisitionCapacity];
        public readonly Voice?[] Voices = new Voice?[VoiceCapacity];
        public readonly PoolSound?[] PoolSounds = new PoolSound?[PoolCapacity];
        public readonly Resolution?[] Resolutions = new Resolution?[ResolutionCapacity];
        public readonly ResolutionKey[] ResolutionKeys = new ResolutionKey[ResolutionCapacity];
        public readonly int[] VoiceIds = new int[VoiceCapacity];
        // Upper word is the acquisition generation; lower word is origin+1.
        // IDs may be assigned after acquisition, so IDs cannot identify reuse.
        public readonly long[] SlotStates = new long[GameAudioLayout.PlayerSoundOwnership.Capacity];
    }

    public SfxNativeTrace(nint moduleStart, nint moduleEnd, GameAudioLayout.SoundData* pool)
    { this.moduleStart = moduleStart; this.moduleEnd = moduleEnd; this.pool = pool; }

    public Acquisition[] Acquisitions => Snapshot(Volatile.Read(ref session)?.Acquisitions);
    public Voice[] Voices => Snapshot(Volatile.Read(ref session)?.Voices);
    public PoolSound[] PoolSounds => Snapshot(Volatile.Read(ref session)?.PoolSounds);
    public Resolution[] Resolutions => Snapshot(Volatile.Read(ref session)?.Resolutions);
    public string? TraceError
    {
        get { var current = Volatile.Read(ref session); return current == null ? null : Volatile.Read(ref current.Error); }
    }

    public void Start()
    {
        var next = new Session();
        var previous = Interlocked.Exchange(ref session, next);
        if (previous != null) Volatile.Write(ref previous.Active, 0);
    }

    public void Stop()
    {
        var current = Volatile.Read(ref session);
        if (current != null) Volatile.Write(ref current.Active, 0);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(nint process, nint address, byte* bytes, nuint size, out nuint read);
    [DllImport("kernel32.dll", EntryPoint = "RtlCaptureStackBackTrace", ExactSpelling = true)]
    private static extern ushort CaptureStack(uint skip, uint count, nint* frames, uint* hash);

    private static void Error(Session current, string message) => Interlocked.CompareExchange(ref current.Error, message, null);
    private static double Seconds(Session current) => Stopwatch.GetElapsedTime(current.Started).TotalSeconds;
    private bool Current(Session current) => Volatile.Read(ref current.Active) != 0 && ReferenceEquals(current, Volatile.Read(ref session));

    private static bool Read(Session current, nint address, Span<byte> bytes)
    {
        if (address != 0)
            fixed (byte* target = bytes)
                if (ReadProcessMemory(-1, address, target, (nuint)bytes.Length, out var read) && read == (nuint)bytes.Length)
                    return true;
        Error(current, "A native diagnostic memory snapshot was unreadable.");
        return false;
    }

    private static nint Pointer(ReadOnlySpan<byte> bytes, int offset) =>
        (nint)BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(offset, 8));

    private static int Reserve(ref int count, int capacity)
    {
        while (true)
        {
            var before = Volatile.Read(ref count);
            if (before >= capacity) return -1;
            if (Interlocked.CompareExchange(ref count, before + 1, before) == before) return before;
        }
    }

    private static int ReserveVoice(Session current, int id)
    {
        if (Interlocked.CompareExchange(ref current.VoiceGate, 1, 0) != 0) return -1;
        try
        {
            // Failed snapshots can release earlier slots, so check all existing
            // IDs before filling a hole. A contending callback simply retries
            // on its next native block instead of waiting here.
            for (var slot = 0; slot < current.VoiceIds.Length; slot++)
                if (Volatile.Read(ref current.VoiceIds[slot]) == id) return -1;
            for (var slot = 0; slot < current.VoiceIds.Length; slot++)
                if (Interlocked.CompareExchange(ref current.VoiceIds[slot], id, 0) == 0) return slot;
            return -1;
        }
        finally { Volatile.Write(ref current.VoiceGate, 0); }
    }

    private int Slot(GameAudioLayout.SoundData* sound)
    {
        var offset = (nuint)sound - (nuint)pool;
        return pool != null && sound != null && offset < (nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * SoundSize) &&
            offset % SoundSize == 0 ? (int)(offset / SoundSize) : -1;
    }

    private static long Acquired(Session current, int slot, byte origin)
    {
        while (true)
        {
            var before = Volatile.Read(ref current.SlotStates[slot]);
            var generation = unchecked((uint)((ulong)before >> 32) + 1);
            if (generation == 0) generation = 1;
            var next = (long)((ulong)generation << 32 | ((uint)origin + 1));
            if (Interlocked.CompareExchange(ref current.SlotStates[slot], next, before) == before) return next;
        }
    }

    public void ObserveAcquisition(GameAudioLayout.SoundData* sound, byte origin, nint scopedActor,
        uint scopedEntity, EnemyActionTag scopedAction)
    {
        var current = Volatile.Read(ref session);
        if (current == null || !Current(current)) return;
        try
        {
            var slot = Slot(sound);
            if (slot < 0) return;
            var state = Acquired(current, slot, origin);
            if (origin != 0) return;
            Span<byte> header = stackalloc byte[0x44];
            if (!Read(current, (nint)sound, header)) return;
            if (state != Volatile.Read(ref current.SlotStates[slot])) return;
            var id = BinaryPrimitives.ReadInt32LittleEndian(header[0x40..]);
            var index = Reserve(ref current.AcquisitionCount, AcquisitionCapacity);
            if (index < 0) return;
            var seconds = Seconds(current);
            var stack = stackalloc nint[StackCapacity];
            var count = CaptureStack(0, StackCapacity, stack, null);
            Span<uint> rvas = stackalloc uint[StackCapacity];
            var length = 0;
            for (var i = 0; i < count && i < StackCapacity; i++)
            {
                var address = stack[i];
                if (moduleStart != 0 && moduleEnd > moduleStart && address >= moduleStart && address < moduleEnd &&
                    (nuint)(address - moduleStart) <= uint.MaxValue)
                    rvas[length++] = (uint)(address - moduleStart);
            }
            var record = new Acquisition(seconds, slot, (uint)((ulong)state >> 32), id, $"{(nint)sound:X}", origin, $"{scopedActor:X}",
                scopedEntity, scopedAction, ImmutableArray.CreateRange(rvas[..length].ToArray()));
            if (Current(current)) Volatile.Write(ref current.Acquisitions[index], record);
        }
        catch (Exception error) { Error(current, error.Message); }
    }

    public void ObserveVoice(GameAudioLayout.SourceVoice* voice, GameAudioLayout.RootSound* root)
    {
        var current = Volatile.Read(ref session);
        if (current == null || !Current(current) || voice == null || root == null) return;
        var index = -1;
        var id = 0;
        var published = false;
        try
        {
            // The caller has already validated SoundOfVoice. RPM also makes a
            // disappearing native object a missed diagnostic, never a bad read.
            Span<byte> rootPrefix = stackalloc byte[0x34];
            if (!Read(current, (nint)root, rootPrefix)) return;
            id = BinaryPrimitives.ReadInt32LittleEndian(rootPrefix[0x0C..]);
            if (id <= 0 || rootPrefix[0x19] != 5) return;
            index = ReserveVoice(current, id);
            if (index < 0) return;
            var seconds = Seconds(current);
            Span<byte> sourceHeader = stackalloc byte[0xB8];
            if (!Read(current, (nint)voice, sourceHeader)) return;
            var callback = Pointer(sourceHeader, 0xB0);
            if ((nuint)callback < 8) return;
            var owner = callback - 8;
            Span<byte> ownerHeader = stackalloc byte[0x28];
            if (!Read(current, owner, ownerHeader) || Pointer(ownerHeader, 0x20) != (nint)voice) return;
            var track = Pointer(ownerHeader, 0x10);
            if (track == 0) return;
            Span<byte> trackHeader = stackalloc byte[0x90];
            if (!Read(current, track, trackHeader) || Pointer(trackHeader, 8) != owner || Pointer(trackHeader, 0x10) != (nint)root) return;
            Span<byte> rootHeader = stackalloc byte[0x480];
            if (!Read(current, (nint)root, rootHeader) || BinaryPrimitives.ReadInt32LittleEndian(rootHeader[0x0C..]) != id ||
                rootHeader[0x19] != 5) return;
            var record = new Voice(seconds, id, rootHeader[0x19], BinaryPrimitives.ReadUInt32LittleEndian(rootHeader[0x30..]),
                $"{(nint)voice:X}", $"{callback:X}", $"{owner:X}", $"{track:X}", $"{(nint)root:X}",
                ImmutableArray.CreateRange(rootHeader.ToArray()), ImmutableArray.CreateRange(trackHeader.ToArray()));
            if (Current(current))
            {
                Volatile.Write(ref current.Voices[index], record);
                published = true;
            }
        }
        catch (Exception error) { Error(current, error.Message); }
        finally
        {
            if (index >= 0 && !published) Interlocked.CompareExchange(ref current.VoiceIds[index], 0, id);
        }
    }

    public void ObserveResolution(nint layout, int reservationId, nint entry)
    {
        var current = Volatile.Read(ref session);
        if (current == null || !Current(current) || entry == 0) return;
        try
        {
            Span<byte> header = stackalloc byte[0x40];
            if (!Read(current, entry, header) || BinaryPrimitives.ReadInt32LittleEndian(header[0x38..]) != reservationId) return;
            var controllerId = BinaryPrimitives.ReadInt32LittleEndian(header[0x10..]);
            var controllerLayout = Pointer(header, 0x18);
            var key = new ResolutionKey(layout, reservationId, controllerId, controllerLayout);
            // No waiting or allocation on an audio callback. Contention can
            // drop optional evidence, but cannot hold up native playback.
            if (Interlocked.CompareExchange(ref current.ResolutionGate, 1, 0) != 0) return;
            try
            {
                for (var i = 0; i < current.ResolutionCount; i++)
                    if (current.ResolutionKeys[i] == key) return;
                if (current.ResolutionCount >= ResolutionCapacity) return;
                var record = new Resolution(Seconds(current), $"{layout:X}", reservationId, $"{entry:X}", controllerId,
                    $"{controllerLayout:X}", ImmutableArray.CreateRange(header.ToArray()));
                if (!Current(current)) return;
                var index = current.ResolutionCount++;
                current.ResolutionKeys[index] = key;
                Volatile.Write(ref current.Resolutions[index], record);
            }
            finally { Volatile.Write(ref current.ResolutionGate, 0); }
        }
        catch (Exception error) { Error(current, error.Message); }
    }

    /// <summary>Framework-thread only; captures no resources or pointees outside the validated pool.</summary>
    public void CapturePool()
    {
        var current = Volatile.Read(ref session);
        if (current == null || !Current(current) || pool == null) return;
        try
        {
            Span<byte> header = stackalloc byte[SoundSize];
            for (var slot = 0; slot < current.SlotStates.Length; slot++)
            {
                var state = Volatile.Read(ref current.SlotStates[slot]);
                if ((uint)state != 1) continue;
                var generation = (uint)((ulong)state >> 32);
                var sound = pool + slot;
                if (!Read(current, (nint)sound, header)) continue;
                if (state != Volatile.Read(ref current.SlotStates[slot])) continue;
                var id = BinaryPrimitives.ReadInt32LittleEndian(header[0x40..]);
                var index = -1;
                for (var i = 0; i < current.PoolCount; i++)
                    if (current.PoolSounds[i] is { } existing && existing.Slot == slot &&
                        existing.Generation == generation && existing.ReservationId == id)
                    { index = i; break; }
                if (index < 0)
                {
                    if (current.PoolCount >= PoolCapacity) continue;
                    index = current.PoolCount++;
                }
                var before = current.PoolSounds[index];
                if (before != null && header.SequenceEqual(before.Header.AsSpan())) continue;
                var seconds = Seconds(current);
                var bytes = ImmutableArray.CreateRange(header.ToArray());
                var firstActive = before?.FirstActiveHeader ?? ImmutableArray<byte>.Empty;
                var firstActiveSeconds = before?.FirstActiveSeconds;
                var resource = Pointer(header, 0x28);
                // Native teardown can clear the resource and playback fields.
                // Keep the first active, resource-backed snapshot separately;
                // it is evidence of that exact acquisition generation, not a
                // claim that any other native readiness fields are valid.
                if (firstActive.IsDefaultOrEmpty && header[0x24] != 0 && resource != 0)
                { firstActive = bytes; firstActiveSeconds = seconds; }
                var record = new PoolSound(seconds, slot, generation, id, $"{(nint)sound:X}", header[0x24],
                    $"{resource:X}", BinaryPrimitives.ReadUInt32LittleEndian(header[0x7C..]),
                    bytes, firstActive, firstActiveSeconds);
                if (Current(current) && state == Volatile.Read(ref current.SlotStates[slot]))
                    Volatile.Write(ref current.PoolSounds[index], record);
            }
        }
        catch (Exception error) { Error(current, error.Message); }
    }

    private static T[] Snapshot<T>(T?[]? records) where T : class
    {
        if (records == null) return Array.Empty<T>();
        var result = new List<T>();
        for (var i = 0; i < records.Length; i++)
            if (Volatile.Read(ref records[i]) is { } record) result.Add(record);
        return result.Count == 0 ? Array.Empty<T>() : result.ToArray();
    }
}
