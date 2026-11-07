// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DualSenseLighting.Core;

internal static class ScreenEffectSourceChecks
{
    private static ScreenEffectSourceSample Sample(long source) => new(source, 20, 30, 40, 50, 60, 9,
        .5f, 2f, 3f, 4f, 5f, 6f);

    internal static void Run(Action<bool, string> check)
    {
        CheckIdentity(check);
        CheckBounds(check);
        CheckContention(check);
        CheckBitsAndRetirement(check);
        CheckAllocations(check);
        CheckRotationIdentity(check);
        CheckRotationLossAndBits(check);
        CheckRotationAllocations(check);
        CheckResetObservation(check);
        CheckResetAllocations(check);
        CheckPosition(check);
    }

    private static void CheckIdentity(Action<bool, string> check)
    {
        var recording = new ScreenEffectSourceRecording(100, 12, 3);
        // Native constructor can call its updater before returning to its detour.
        recording.OnConstruction(10, 101);
        recording.OnSample(Sample(10), 102);
        recording.OnSample(Sample(10), 103);
        recording.OnSample(Sample(11), 104); // Already live when tracing started.
        recording.OnConstruction(10, 105); // Same allocation address reconstructed.
        recording.OnSample(Sample(10), 106);
        recording.OnGap(107); // Permission/context loss is not destruction.
        recording.OnSample(Sample(10), 108);
        var snapshot = recording.Complete(); var events = snapshot.Events;
        check(events.Length == 8 && events[0].StartKnown && events[1].StartKnown &&
            events[0].Generation == events[1].Generation && events[1].Generation == events[2].Generation,
            "A constructor's nested/steady updater lost its construction identity");
        check(!events[3].StartKnown && events[3].Generation != events[2].Generation,
            "First observation invented a construction start or merged different source pointers");
        check(events[4].Generation > events[3].Generation && events[4].Generation == events[5].Generation,
            "Reconstructing the same pointer retained its previous generation");
        check(events[6].Kind == ScreenEffectSourceRecording.EventKind.Gap && events[7].Epoch > events[5].Epoch &&
            !events[7].StartKnown && events[7].Generation > events[5].Generation,
            "A permission gap retained construction confidence or reused an earlier observation identity");
        check(events[1].Fields.Owner == 20 && events[1].Fields.Parent == 30 && events[1].Fields.InheritedResource == 40 &&
            events[1].Fields.Wrapper == 50 && events[1].Fields.AuthoredData == 60 && events[1].Fields.Type == 9 &&
            events[1].Fields.Arg2Bits == BitConverter.SingleToUInt32Bits(2f),
            "Copied source metadata or updater arguments changed");
        foreach (var value in events)
            check(value.End == ScreenEffectSourceRecording.EndEvidence.Unknown,
                "A source observation or context gap invented destruction evidence");
    }

    private static void CheckBounds(Action<bool, string> check)
    {
        var full = new ScreenEffectSourceRecording(0, 2, 2);
        full.OnConstruction(1, 1); full.OnSample(Sample(1), 2);
        full.OnSample(Sample(1), 3); full.OnConstruction(2, 4);
        var snapshot = full.Complete();
        check(snapshot.Events.Length == 2 && snapshot.Events[0].Clock == 1 && snapshot.Events[1].Clock == 2 &&
            snapshot.Lost == 2 && snapshot.Overflow == 2 && snapshot.Truncated,
            "Full event recording overwrote evidence or hid exact truncation counts");
        var sources = new ScreenEffectSourceRecording(0, 5, 2);
        sources.OnConstruction(1, 1); sources.OnConstruction(3, 2); // Deliberate hash collision.
        sources.OnSample(Sample(5), 3);
        snapshot = sources.Snapshot();
        check(snapshot.Events.Length == 2 && snapshot.Lost == 1 && snapshot.Overflow == 1 && snapshot.Truncated,
            "Source capacity silently evicted a known identity or admitted an untracked source");
        sources.OnSample(Sample(1), 4);
        snapshot = sources.Complete();
        check(snapshot.Events.Length == 3 && !snapshot.Events[2].StartKnown &&
            snapshot.Events[2].Epoch > snapshot.Events[1].Epoch,
            "A dropped possible construction retained source generation confidence");
    }

    private static void CheckContention(Action<bool, string> check)
    {
        var recording = new ScreenEffectSourceRecording(0, 4, 2);
        recording.OnConstruction(1, 1);
        var sync = typeof(ScreenEffectSourceRecording).GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(recording)!;
        Task attempted;
        lock (sync)
        {
            attempted = Task.Run(() => recording.OnConstruction(1, 2));
            check(attempted.Wait(TimeSpan.FromSeconds(5)), "Source callback waited for a contended recording lock");
        }
        attempted.GetAwaiter().GetResult();
        recording.OnSample(Sample(1), 3);
        var snapshot = recording.Complete();
        check(snapshot.Lost == 1 && snapshot.Overflow == 0 && !snapshot.Truncated && snapshot.Events.Length == 2 &&
            !snapshot.Events[1].StartKnown && snapshot.Events[1].Epoch > snapshot.Events[0].Epoch,
            "Contended constructor loss was hidden or later samples retained false construction confidence");
    }

    private static void CheckBitsAndRetirement(Action<bool, string> check)
    {
        var recording = new ScreenEffectSourceRecording(0, 3, 1);
        var nan = BitConverter.UInt32BitsToSingle(0x7fc01234);
        recording.OnSample(Sample(1) with { Arg1 = nan, Field1D4 = float.NegativeInfinity }, 1);
        var before = recording.Snapshot();
        recording.OnSample(Sample(1), 2);
        check(before.Events.Length == 1, "Snapshot retained a mutable recording buffer view");
        var completed = recording.Complete();
        recording.OnConstruction(1, 3); recording.OnGap(4); recording.OnSample(Sample(1), 5);
        var after = recording.Complete();
        check(after.Retired && after.Events.Length == 2 && after.Lost == 0 && after.Events[0] == completed.Events[0],
            "Retired capture accepted callbacks or changed detached evidence");
        check(completed.Events[0].Fields.Arg1Bits == 0x7fc01234 &&
            completed.Events[0].Fields.Field1D4Bits == BitConverter.SingleToUInt32Bits(float.NegativeInfinity) &&
            JsonSerializer.Serialize(completed).Contains("Arg1Bits", StringComparison.Ordinal),
            "Nonfinite native values were lost or made default JSON publication fail");
    }

    private static SecondaryVectorSample Rotation(long source, bool scoped = true) =>
        new(source, 20, 30, 1, scoped, .25f, -.5f, 0);

    private static void CheckRotationIdentity(Action<bool, string> check)
    {
        var recording = new ScreenEffectSourceRecording(0, 9, 2);
        recording.OnConstruction(10, 1);
        recording.OnRotation(Rotation(10), 2);
        recording.OnSample(Sample(10), 3);
        recording.OnRotation(Rotation(10), 4);
        // No source slots remain. Unscoped writes still have no identity to admit.
        recording.OnRotation(Rotation(0, false), 5);
        recording.OnRotation(Rotation(0, false) with { Context = 99 }, 6);
        recording.OnRotation(Rotation(42, false), 7);
        recording.OnRotation(Rotation(0), 8); // Missing source defeats a claimed scope.
        var snapshot = recording.Complete(); var events = snapshot.Events;
        check(events.Length == 8 && snapshot.Lost == 0 && snapshot.Overflow == 0,
            "Unscoped rotation writes consumed identity slots or aliased their missing source");
        check(events[1].Family == ScreenEffectSourceRecording.SourceFamily.SecondaryRotation &&
            events[0].Family == ScreenEffectSourceRecording.SourceFamily.AvfxQuake &&
            !events[1].StartKnown && events[1].Generation != events[0].Generation &&
            events[2].StartKnown && events[2].Generation == events[0].Generation &&
            events[3].Generation == events[1].Generation && !events[3].StartKnown,
            "Same-address source families inherited construction confidence or lost stable identities");
        for (var i = 4; i < events.Length; i++)
            check(events[i].Generation == 0 && !events[i].StartKnown && events[i].Rotation.HasValue,
                "An unscoped or null-source setter invented an observed source generation");
        check(events[1].Kind == ScreenEffectSourceRecording.EventKind.SecondaryRotation &&
            events[1].Rotation is { Source: 10, Context: 20, Config: 30, Slot: 1, Scoped: true } &&
            events[4].Rotation!.Value.Context != events[5].Rotation!.Value.Context && events[0].Rotation == null,
            "Rotation schema lost exact producer/context/config/slot scope or contaminated AVFX payloads");
    }

    private static void CheckRotationLossAndBits(Action<bool, string> check)
    {
        var recording = new ScreenEffectSourceRecording(0, 3, 2);
        var nan = BitConverter.UInt32BitsToSingle(0x7fc04567);
        recording.OnRotation(Rotation(1) with { X = nan, Y = float.PositiveInfinity, Z = -0f }, 1);
        recording.OnGap(2);
        recording.OnRotation(Rotation(1), 3);
        recording.OnRotation(Rotation(2), 4);
        var snapshot = recording.Complete(); var first = snapshot.Events[0];
        check(first.Rotation is { XBits: 0x7fc04567, YBits: 0x7f800000, ZBits: 0x80000000 } &&
            JsonSerializer.Serialize(snapshot).Contains("XBits", StringComparison.Ordinal),
            "Rotation native float bits changed or nonfinite evidence broke JSON publication");
        check(snapshot.Lost == 1 && snapshot.Overflow == 1 && snapshot.Truncated &&
            snapshot.Events[2].Epoch > first.Epoch && snapshot.Events[2].Generation > first.Generation &&
            !snapshot.Events[2].StartKnown,
            "Rotation recording hid overflow or treated a context gap as continuous known lifetime");
        recording.OnRotation(Rotation(1), 5);
        check(recording.Snapshot().Events.Length == 3 && recording.Snapshot().Lost == 1,
            "Retired rotation callbacks changed captured events or loss counts");
    }

    private static void CheckResetObservation(Action<bool, string> check)
    {
        var recording = new ScreenEffectSourceRecording(0, 5, 2);
        recording.OnConstruction(1, 1); // Same pointer, different source family.
        recording.OnRotation(Rotation(1), 2);
        recording.OnReset(1, 20, 30, 0x7fc01234, uint.MaxValue, 3);
        recording.OnRotation(Rotation(1), 4);
        recording.OnReset(0, 99, 100, 0x80000000, 0, 5); // No remaining source slot needed.
        recording.OnReset(1, 20, 30, 0, 1, 6);
        var snapshot = recording.Complete(); var events = snapshot.Events;
        check(events.Length == 5 && events[2].Kind == ScreenEffectSourceRecording.EventKind.SecondaryReset &&
            events[2].Family == ScreenEffectSourceRecording.SourceFamily.SecondaryRotation &&
            events[2].Generation == events[1].Generation && events[3].Generation == events[1].Generation &&
            !events[2].StartKnown && !events[3].StartKnown && events[2].Epoch == events[1].Epoch &&
            events[3].Epoch == events[1].Epoch && events[0].Generation != events[2].Generation,
            "A native reset call invented a restart, inherited AVFX birth, or broke observed source continuity");
        check(events[2].Reset is { Source: 1, Context: 20, Config: 30, PreviousCursorBits: 0x7fc01234, State: uint.MaxValue } &&
            events[2].Rotation == null && events[1].Reset == null && events[4].Generation == 0 &&
            !events[4].StartKnown && events[4].Reset is { PreviousCursorBits: 0x80000000 } &&
            JsonSerializer.Serialize(snapshot).Contains("PreviousCursorBits", StringComparison.Ordinal),
            "Reset observation lost raw scalar bits, invented a null-source identity, or contaminated another payload");
        check(snapshot.Lost == 1 && snapshot.Overflow == 1 && snapshot.Truncated &&
            events[2].End == ScreenEffectSourceRecording.EndEvidence.Unknown,
            "Reset capture hid overflow or claimed a native control call proved source end");
        recording.OnReset(1, 20, 30, 0, 0, 7);
        var after = recording.Snapshot();
        check(after.Events.Length == 5 && after.Lost == 1 && after.Overflow == 1,
            "Retired reset observation changed evidence or loss counts");
    }

    private static void CheckPosition(Action<bool, string> check)
    {
        var recording = new ScreenEffectSourceRecording(0, 7, 1);
        var value = Rotation(1) with { X = BitConverter.UInt32BitsToSingle(0x7fc01234), Y = float.NegativeInfinity, Z = -0f };
        recording.OnPosition(value, 1);
        recording.OnRotation(Rotation(1), 2);
        recording.OnReset(1, 20, 30, 7, 8, 3);
        recording.OnPosition(Rotation(1), 4);
        recording.OnPosition(Rotation(0, false), 5);
        recording.OnPosition(Rotation(2, false), 6);
        recording.OnPosition(Rotation(0), 7);
        recording.OnPosition(Rotation(1), 8);
        var snapshot = recording.Complete(); var events = snapshot.Events;
        check(events.Length == 7 && events[0].Kind == ScreenEffectSourceRecording.EventKind.SecondaryPosition &&
            events[0].Family == ScreenEffectSourceRecording.SourceFamily.SecondaryRotation &&
            events[0].Generation == events[1].Generation && events[1].Generation == events[2].Generation &&
            events[2].Generation == events[3].Generation && !events[0].StartKnown && !events[3].StartKnown,
            "Position/rotation/reset scheduler observations lost their shared unknown-birth identity");
        check(events[0].Rotation == null && events[0].Reset == null && events[1].Position == null &&
            events[2].Position == null && events[0].Position is { Source: 1, Context: 20, Config: 30, Slot: 1,
                Scoped: true, XBits: 0x7fc01234, YBits: 0xff800000, ZBits: 0x80000000 },
            "Position payload lost exact scalar bits or mislabeled another event type");
        var roundtrip = JsonSerializer.Deserialize<ScreenEffectSourceRecording.RecordingSnapshot>(JsonSerializer.Serialize(snapshot));
        check(roundtrip != null && roundtrip.Events[0].Position == events[0].Position && roundtrip.Events[1].Position == null,
            "Position payload changed in JSON roundtrip or populated a rotation event");
        for (var index = 4; index < 7; index++)
            check(events[index].Generation == 0 && !events[index].StartKnown,
                "Unscoped/null-source position writes consumed identity capacity or invented one shared source");
        check(snapshot.Lost == 1 && snapshot.Overflow == 1 && snapshot.Truncated,
            "Position capacity overflow overwrote earlier evidence or hid loss");
        recording.OnPosition(value, 9);
        check(recording.Snapshot().Events.Length == 7 && recording.Snapshot().Lost == 1,
            "Retired position writes changed captured evidence");
        var allocation = new ScreenEffectSourceRecording(0);
        Action callback = () => allocation.OnPosition(value, 1);
        var bytes = AllocationChecks.MeasureAllocations(callback, warmup: 100, iterations: 1000);
        check(bytes == 0 && allocation.Complete().Events.Length == 1100,
            $"Position callbacks allocated {bytes} bytes or lost admitted events");
    }

    private static void CheckResetAllocations(Action<bool, string> check)
    {
        var recording = new ScreenEffectSourceRecording(0);
        Action callback = () => recording.OnReset(1, 20, 30, 0x3f800000, 2, 1);
        var allocated = AllocationChecks.MeasureAllocations(callback, warmup: 100, iterations: 1000);
        var snapshot = recording.Complete();
        check(allocated == 0 && snapshot.Events.Length == 1100 && snapshot.Lost == 0 &&
            !snapshot.Events[0].StartKnown && snapshot.Events[0].Generation == snapshot.Events[1099].Generation,
            $"Reset callbacks allocated {allocated} bytes or invented lifetime changes");
    }

    private static void CheckRotationAllocations(Action<bool, string> check)
    {
        var recording = new ScreenEffectSourceRecording(0);
        var value = Rotation(1);
        Action callback = () => recording.OnRotation(value, 1);
        var allocated = AllocationChecks.MeasureAllocations(callback, warmup: 100, iterations: 1000);
        var snapshot = recording.Complete();
        check(allocated == 0 && snapshot.Events.Length == 1100 && snapshot.Lost == 0,
            $"Rotation callbacks allocated {allocated} bytes or lost admitted events");
    }

    private static void CheckAllocations(Action<bool, string> check)
    {
        var recording = new ScreenEffectSourceRecording(0);
        var value = Sample(1);
        Action callback = () => recording.OnSample(value, 1);
        var allocated = AllocationChecks.MeasureAllocations(callback, warmup: 100, iterations: 1000);
        check(allocated == 0 && recording.Complete().Events.Length == 1100,
            $"Source callbacks allocated {allocated} bytes or lost admitted events");
    }
}
