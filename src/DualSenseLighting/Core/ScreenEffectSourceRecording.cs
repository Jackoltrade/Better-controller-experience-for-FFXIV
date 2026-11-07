// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Immutable;
using System.Threading;

namespace DualSenseLighting.Core;

internal readonly record struct ScreenEffectSourceSample(long Source, long Owner, long Parent,
    long InheritedResource, long Wrapper, long AuthoredData, uint Type,
    float Arg1, float Arg2, float Field1C8, float Field1CC, float Field1D0, float Field1D4);

// Exact float bits retain nonfinite native evidence without nonfinite JSON numbers.
internal readonly record struct ScreenEffectSourceFields(long Source, long Owner, long Parent,
    long InheritedResource, long Wrapper, long AuthoredData, uint Type,
    uint Arg1Bits, uint Arg2Bits, uint Field1C8Bits, uint Field1CCBits, uint Field1D0Bits, uint Field1D4Bits)
{
    internal static ScreenEffectSourceFields From(ScreenEffectSourceSample value) => new(value.Source,
        value.Owner, value.Parent, value.InheritedResource, value.Wrapper, value.AuthoredData, value.Type,
        BitConverter.SingleToUInt32Bits(value.Arg1), BitConverter.SingleToUInt32Bits(value.Arg2),
        BitConverter.SingleToUInt32Bits(value.Field1C8), BitConverter.SingleToUInt32Bits(value.Field1CC),
        BitConverter.SingleToUInt32Bits(value.Field1D0), BitConverter.SingleToUInt32Bits(value.Field1D4));
}

internal readonly record struct SecondaryVectorSample(long Source, long Context, long Config,
    int Slot, bool Scoped, float X, float Y, float Z);

internal readonly record struct SecondaryVectorFields(long Source, long Context, long Config,
    int Slot, bool Scoped, uint XBits, uint YBits, uint ZBits)
{
    internal static SecondaryVectorFields From(SecondaryVectorSample value) => new(value.Source,
        value.Context, value.Config, value.Slot, value.Scoped, BitConverter.SingleToUInt32Bits(value.X),
        BitConverter.SingleToUInt32Bits(value.Y), BitConverter.SingleToUInt32Bits(value.Z));
}

// A native control call is evidence of that call, not a playback restart or end.
internal readonly record struct SecondaryResetFields(long Source, long Context, long Config,
    uint PreviousCursorBits, uint State);

/// <summary>One opt-in bounded capture. Callers copy native fields; this owner never dereferences pointers.</summary>
internal sealed class ScreenEffectSourceRecording
{
    internal const int EventCapacity = 16384, SourceCapacity = 512;
    internal enum EventKind { Construction, Sample, Gap, SecondaryRotation, SecondaryReset, SecondaryPosition }
    // Legacy serialized family name: this scheduler owns position, rotation and reset observations.
    internal enum SourceFamily { AvfxQuake, SecondaryRotation }
    internal enum EndEvidence { Unknown }
    internal readonly record struct SourceEvent(long Sequence, long Clock, long Epoch, long Generation,
        bool StartKnown, EventKind Kind, ScreenEffectSourceFields Fields,
        SourceFamily Family = SourceFamily.AvfxQuake, SecondaryVectorFields? Rotation = null,
        SecondaryResetFields? Reset = null, SecondaryVectorFields? Position = null)
    {
        public EndEvidence End => EndEvidence.Unknown;
    }
    internal sealed record RecordingSnapshot(long StartedClock, int EventLimit, int SourceLimit,
        long Lost, long Overflow, bool Truncated, bool Retired, ImmutableArray<SourceEvent> Events);
    private struct SourceSlot
    {
        internal bool Occupied, StartKnown;
        internal long Source, Generation;
        internal SourceFamily Family;
    }
    private readonly object sync = new();
    private readonly long startedClock;
    private readonly SourceEvent[] events;
    private readonly SourceSlot[] sources;
    private int count, retired, confidenceGap;
    private long epoch = 1, generation, lost, overflow;

    internal ScreenEffectSourceRecording(long startedClock) : this(startedClock, EventCapacity, SourceCapacity) { }
    internal ScreenEffectSourceRecording(long startedClock, int eventCapacity, int sourceCapacity)
    {
        if (eventCapacity <= 0 || eventCapacity > EventCapacity) throw new ArgumentOutOfRangeException(nameof(eventCapacity));
        if (sourceCapacity <= 0 || sourceCapacity > SourceCapacity) throw new ArgumentOutOfRangeException(nameof(sourceCapacity));
        this.startedClock = startedClock;
        events = new SourceEvent[eventCapacity];
        sources = new SourceSlot[sourceCapacity];
    }

    internal void OnConstruction(long source, long clock) => Record(source, clock, EventKind.Construction, default);
    internal void OnSample(ScreenEffectSourceSample value, long clock) => Record(value.Source, clock, EventKind.Sample,
        ScreenEffectSourceFields.From(value));
    internal void OnRotation(SecondaryVectorSample value, long clock) => Record(value.Source, clock,
        EventKind.SecondaryRotation, default, SourceFamily.SecondaryRotation, SecondaryVectorFields.From(value));
    internal void OnPosition(SecondaryVectorSample value, long clock) => Record(value.Source, clock,
        EventKind.SecondaryPosition, default, SourceFamily.SecondaryRotation,
        position: SecondaryVectorFields.From(value));
    internal void OnReset(long source, long context, long config, uint previousCursorBits, uint state, long clock) =>
        Record(source, clock, EventKind.SecondaryReset, default, SourceFamily.SecondaryRotation,
            reset: new SecondaryResetFields(source, context, config, previousCursorBits, state));
    internal void OnGap(long clock) => Record(0, clock, EventKind.Gap, default);

    private void Record(long source, long clock, EventKind kind, ScreenEffectSourceFields fields,
        SourceFamily family = SourceFamily.AvfxQuake, SecondaryVectorFields? rotation = null, SecondaryResetFields? reset = null,
        SecondaryVectorFields? position = null)
    {
        if (Volatile.Read(ref retired) != 0) return;
        if (!Monitor.TryEnter(sync))
        {
            Interlocked.Exchange(ref confidenceGap, 1);
            Interlocked.Increment(ref lost);
            return;
        }
        try
        {
            if (Volatile.Read(ref retired) != 0) return;
            if (Interlocked.Exchange(ref confidenceGap, 0) != 0)
            { Array.Clear(sources); epoch++; }
            if (count == events.Length) { DropOverflow(); return; }
            if (kind == EventKind.Gap)
            {
                Array.Clear(sources);
                epoch++;
                events[count] = new(count + 1L, clock, epoch, 0, false, kind, default);
                count++;
                return;
            }
            // A setter without producer scope supplies no source identity to retain.
            if (kind == EventKind.SecondaryRotation && (rotation?.Scoped != true || source == 0) ||
                kind == EventKind.SecondaryReset && source == 0 ||
                kind == EventKind.SecondaryPosition && (position?.Scoped != true || source == 0))
            {
                events[count] = new(count + 1L, clock, epoch, 0, false, kind, default, family, rotation, reset, position);
                count++;
                return;
            }
            var start = (int)((ulong)source % (uint)sources.Length);
            for (var probe = 0; probe < sources.Length; probe++)
            {
                ref var slot = ref sources[(start + probe) % sources.Length];
                if (slot.Occupied && (slot.Source != source || slot.Family != family)) continue;
                if (!slot.Occupied || kind == EventKind.Construction)
                {
                    slot = new SourceSlot { Occupied = true, Source = source, Family = family, Generation = ++generation,
                        StartKnown = kind == EventKind.Construction };
                }
                if (kind == EventKind.Construction) fields = new ScreenEffectSourceFields { Source = source };
                events[count] = new(count + 1L, clock, epoch, slot.Generation, slot.StartKnown, kind, fields, family, rotation, reset, position);
                count++;
                return;
            }
            DropOverflow(); // Never evict a retained identity to admit a new source.
        }
        finally { Monitor.Exit(sync); }
    }

    private void DropOverflow()
    {
        Interlocked.Exchange(ref confidenceGap, 1);
        Interlocked.Increment(ref lost); Interlocked.Increment(ref overflow);
    }

    internal RecordingSnapshot Complete()
    {
        Retire();
        return Snapshot();
    }
    internal void Retire() => Interlocked.Exchange(ref retired, 1);
    internal RecordingSnapshot Snapshot()
    {
        lock (sync)
        {
            return new(startedClock, events.Length, sources.Length, Interlocked.Read(ref lost),
                Interlocked.Read(ref overflow), Interlocked.Read(ref overflow) != 0,
                Volatile.Read(ref retired) != 0, ImmutableArray.Create(events, 0, count));
        }
    }
}
