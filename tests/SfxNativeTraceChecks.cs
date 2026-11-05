// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static unsafe class SfxNativeTraceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        using var process = Process.GetCurrentProcess();
        var module = process.MainModule!;
        var moduleStart = module.BaseAddress; var moduleEnd = moduleStart + module.ModuleMemorySize;
        CheckAcquisitionProvenance(check, fixture, moduleStart, moduleEnd);
        CheckPoolSnapshots(check, fixture, moduleStart, moduleEnd);
        CheckFirstActiveHeader(check, fixture, moduleStart, moduleEnd);
        CheckSlotReuse(check, fixture, moduleStart, moduleEnd);
        CheckVoices(check, fixture, moduleStart, moduleEnd);
        CheckResolutions(check, fixture, moduleStart, moduleEnd);
        CheckDisabledAndUnreadable(check, fixture, moduleStart, moduleEnd);
    }

    private static bool AddressIs(string value, nint address) =>
        string.Equals(value, address.ToString("X"), StringComparison.OrdinalIgnoreCase);

    private static void CheckAcquisitionProvenance(Action<bool, string> check, Fixture fixture, nint start, nint end)
    {
        fixture.ClearPool();
        var trace = new SfxNativeTrace(start, end, fixture.Pool); trace.Start();
        var tag = new EnemyActionTag(EnemyActionKind.Skill, 11212, 6429, EnemyActionPhase.ReleasedEffect,
            EnemySoundBinding.CasterOrigin, HasAttackType: true);
        fixture.Pool[0].SoundId = 701;
        foreach (var origin in new byte[] { 1, 2, 3, 255 })
            trace.ObserveAcquisition(fixture.Pool, origin, 0xABC, 4321, tag);
        check(trace.Acquisitions.Length == 0, "Native diagnostics collected already-attributed nonzero-origin acquisitions");
        trace.ObserveAcquisition(fixture.Pool, 0, 0xABC, 4321, tag);
        fixture.Pool[1].SoundId = 702;
        trace.ObserveAcquisition(fixture.Pool + 1, 0, 0, 0, default);
        var snapshot = trace.Acquisitions;
        check(snapshot.Length == 2 && snapshot[0].Slot == 0 && snapshot[0].ReservationId == 701 && snapshot[0].Origin == 0 &&
            AddressIs(snapshot[0].SlotAddress, (nint)fixture.Pool) && AddressIs(snapshot[0].ScopedActor, 0xABC) &&
            snapshot[0].ScopedEntity == 4321 && snapshot[0].ScopedAction == tag,
            "An unattributed acquisition lost its exact native slot or scoped action/actor provenance");
        check(snapshot[1].Slot == 1 && snapshot[1].ReservationId == 702 && snapshot[1].ScopedEntity == 0 &&
            AddressIs(snapshot[1].ScopedActor, 0) && snapshot[1].ScopedAction == default,
            "Unknown acquisition scope invented an actor or skill instead of retaining missing evidence");
        check(snapshot.All(item => !item.StackRvas.IsDefault && item.StackRvas.All(rva => rva < (ulong)(end - start))),
            "Native acquisition call stacks retained addresses outside the supplied module range");
        fixture.Pool[0].SoundId = 999;
        trace.Stop();
        trace.ObserveAcquisition(fixture.Pool, 0, 0xDEF, 5555, default);
        trace.Start();
        check(trace.Acquisitions.Length == 0 && snapshot[0].ReservationId == 701 && snapshot[0].ScopedAction == tag,
            "Stopping/resetting native diagnostics mutated a detached acquisition snapshot");
        for (var i = 0; i < 200; i++)
        {
            fixture.Pool[i].SoundId = 1000 + i;
            trace.ObserveAcquisition(fixture.Pool + i, 0, 0, 0, default);
        }
        check(trace.Acquisitions.Length == 128 && trace.Acquisitions.Select(item => item.Slot).Distinct().Count() == 128,
            "Unattributed acquisition diagnostics exceeded their bound or lost all eligible samples");
        trace.Stop();
    }

    private static void CheckPoolSnapshots(Action<bool, string> check, Fixture fixture, nint start, nint end)
    {
        fixture.ClearPool();
        var trace = new SfxNativeTrace(start, end, fixture.Pool); trace.Start();
        for (var i = 0; i < 4; i++)
        {
            fixture.Pool[i].IsActive = 1; fixture.Pool[i].SoundId = 2000 + i;
            fixture.Pool[i].ResourceHandle = 1; fixture.Pool[i].SoundNumber = 9;
            trace.ObserveAcquisition(fixture.Pool + i, (byte)i, 0, 0, default);
        }
        trace.CapturePool();
        var first = trace.PoolSounds;
        check(first.Length == 1 && first[0].Slot == 0 && first[0].ReservationId == 2000 && first[0].IsActive == 1 &&
            AddressIs(first[0].ResourceHandle, 1) && first[0].SoundNumber == 9 && first[0].Header.Length == sizeof(GameAudioLayout.SoundData) &&
            BitConverter.ToInt32(first[0].Header.AsSpan().Slice(0x40, 4)) == 2000 && trace.TraceError == null,
            "Pool diagnostics lost raw origin-zero headers, included scoped origins, or chased a resource address");
        fixture.Pool[0].ResourceHandle = 2; fixture.Pool[0].SoundNumber = 10;
        trace.CapturePool();
        var updated = trace.PoolSounds;
        check(updated.Length == 1 && AddressIs(updated[0].ResourceHandle, 2) && updated[0].SoundNumber == 10 &&
            BitConverter.ToInt64(updated[0].Header.AsSpan().Slice(0x28, 8)) == 2 &&
            AddressIs(first[0].ResourceHandle, 1) && first[0].SoundNumber == 9 &&
            BitConverter.ToInt64(first[0].Header.AsSpan().Slice(0x28, 8)) == 1,
            "A late resource update was discarded or mutated a previously captured pool header");
        ((byte*)fixture.Pool)[0x10] = 0x7B;
        trace.CapturePool();
        check(trace.PoolSounds.Length == 1 && trace.PoolSounds[0].Header[0x10] == 0x7B && updated[0].Header[0x10] == 0,
            "Native readiness bytes were ignored when the public resource fields stayed unchanged");
        trace.Stop();
        var frozen = trace.PoolSounds;
        ((byte*)fixture.Pool)[0x10] = 0xA5;
        trace.CapturePool();
        check(trace.PoolSounds[0].Header[0x10] == 0x7B && frozen[0].Header[0x10] == 0x7B,
            "Disabled pool capture changed a completed segment's detached header");

        trace.Start(); fixture.ClearPool();
        for (var i = 0; i < GameAudioLayout.PlayerSoundOwnership.Capacity; i++)
        {
            fixture.Pool[i].IsActive = 1; fixture.Pool[i].SoundId = 3000 + i;
            trace.ObserveAcquisition(fixture.Pool + i, 0, 0, 0, default);
        }
        trace.CapturePool();
        var bounded = trace.PoolSounds;
        check(bounded.Length == 128 && bounded.Select(item => (item.Slot, item.ReservationId)).Distinct().Count() == 128,
            "Pool header collection grew without a bound or failed to keep eligible native reservations");
        var slot = bounded[0].Slot; fixture.Pool[slot].ResourceHandle = 3; fixture.Pool[slot].SoundNumber = 11;
        trace.CapturePool();
        check(trace.PoolSounds.Length == 128 && trace.PoolSounds.Any(item => item.Slot == slot &&
            AddressIs(item.ResourceHandle, 3) && item.SoundNumber == 11),
            "Reaching the pool observation limit prevented updates to an already captured reservation");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(frozen));
        check(json.RootElement[0].GetProperty("Header").ValueKind == JsonValueKind.Array,
            "Raw diagnostic pool bytes serialized as an opaque value instead of an inspectable JSON array");
        check(frozen.Length == 1 && frozen[0].Header[0x10] == 0x7B,
            "Starting another diagnostic segment recycled a saved pool snapshot");
        trace.Stop();
    }

    private static void CheckFirstActiveHeader(Action<bool, string> check, Fixture fixture, nint start, nint end)
    {
        fixture.ClearPool();
        var trace = new SfxNativeTrace(start, end, fixture.Pool); trace.Start();
        fixture.Pool->SoundId = 9000;
        trace.ObserveAcquisition(fixture.Pool, 0, 0, 0, default);
        fixture.Pool->IsActive = 1; trace.CapturePool();
        check(trace.PoolSounds.Single().FirstActiveHeader.IsEmpty && trace.PoolSounds.Single().FirstActiveSeconds == null,
            "An active reservation without a resource fabricated a first-active snapshot");
        fixture.Pool->IsActive = 0; fixture.Pool->ResourceHandle = 1; trace.CapturePool();
        check(trace.PoolSounds.Single().FirstActiveHeader.IsEmpty && trace.PoolSounds.Single().FirstActiveSeconds == null,
            "An inactive reservation with a resource fabricated a first-active snapshot");

        fixture.Pool->IsActive = 2;
        // +0x98 is retained as raw evidence; this test assigns no meaning to it.
        *(int*)((byte*)fixture.Pool + 0x98) = 42;
        trace.CapturePool();
        var first = trace.PoolSounds.Single();
        check(first.FirstActiveHeader.Length == sizeof(GameAudioLayout.SoundData) && first.FirstActiveHeader[0x24] == 2 &&
            BitConverter.ToInt32(first.FirstActiveHeader.AsSpan().Slice(0x98, 4)) == 42 &&
            first.FirstActiveSeconds is >= 0 && first.FirstActiveSeconds <= first.Seconds,
            "The first nonzero-active resource snapshot lost its raw header or observation time");
        fixture.Pool->IsActive = 1; fixture.Pool->ResourceHandle = 2;
        *(int*)((byte*)fixture.Pool + 0x98) = 88; trace.CapturePool();
        fixture.Pool->IsActive = 0; fixture.Pool->ResourceHandle = 0;
        *(int*)((byte*)fixture.Pool + 0x98) = -1; trace.CapturePool();
        var stopped = trace.PoolSounds.Single();
        check(stopped.Header[0x24] == 0 && BitConverter.ToInt64(stopped.Header.AsSpan().Slice(0x28, 8)) == 0 &&
            BitConverter.ToInt32(stopped.Header.AsSpan().Slice(0x98, 4)) == -1 &&
            stopped.FirstActiveHeader.SequenceEqual(first.FirstActiveHeader) && stopped.FirstActiveSeconds == first.FirstActiveSeconds &&
            BitConverter.ToInt32(first.Header.AsSpan().Slice(0x98, 4)) == 42,
            "A later active update or native teardown erased first-active evidence or changed a detached header");

        fixture.Pool->SoundId = 9001; trace.CapturePool();
        var lateId = trace.PoolSounds.Single(item => item.ReservationId == 9001);
        check(lateId.Generation == first.Generation && lateId.FirstActiveHeader.IsEmpty && lateId.FirstActiveSeconds == null,
            "A later native ID inherited first-active evidence from a different reservation");
        fixture.Pool->IsActive = 1; fixture.Pool->ResourceHandle = 3; trace.CapturePool();
        fixture.Pool->IsActive = 0; fixture.Pool->ResourceHandle = 0;
        trace.ObserveAcquisition(fixture.Pool, 0, 0, 0, default); trace.CapturePool();
        var reused = trace.PoolSounds.Single(item => item.ReservationId == 9001 && item.Generation != lateId.Generation);
        check(reused.FirstActiveHeader.IsEmpty && reused.FirstActiveSeconds == null,
            "A reused native slot/ID inherited first-active evidence from an earlier acquisition generation");
        trace.Stop(); var frozen = trace.PoolSounds;
        trace.Start(); trace.ObserveAcquisition(fixture.Pool, 0, 0, 0, default); trace.CapturePool();
        check(trace.PoolSounds.Single().FirstActiveHeader.IsEmpty && trace.PoolSounds.Single().FirstActiveSeconds == null &&
            frozen.Single(item => item.ReservationId == 9000).FirstActiveHeader.SequenceEqual(first.FirstActiveHeader),
            "Restarting diagnostics inherited first-active state or recycled a completed snapshot's immutable bytes");
        trace.Stop();
    }

    private static void CheckSlotReuse(Action<bool, string> check, Fixture fixture, nint start, nint end)
    {
        fixture.ClearPool();
        var trace = new SfxNativeTrace(start, end, fixture.Pool); trace.Start();
        fixture.Pool->IsActive = 1; fixture.Pool->ResourceHandle = 1;
        trace.ObserveAcquisition(fixture.Pool, 0, 0xABC, 4321, default);
        var acquired = trace.Acquisitions.Single();
        fixture.Pool->SoundId = 8000;
        trace.CapturePool();
        var assigned = trace.PoolSounds.Single(item => item.ReservationId == 8000);
        check(acquired.ReservationId == 0 && assigned.Generation == acquired.Generation && assigned.Slot == acquired.Slot,
            "A native ID assigned after acquisition lost its original slot generation");
        var count = trace.PoolSounds.Length;
        trace.ObserveAcquisition(fixture.Pool, 2, 0xDEF, 9876, default);
        fixture.Pool->ResourceHandle = 2; fixture.Pool->SoundNumber = 99;
        trace.CapturePool();
        check(trace.PoolSounds.Length == count && trace.PoolSounds.All(item => item.ResourceHandle != "2") &&
            trace.Acquisitions.Length == 1,
            "A nonzero-origin reuse inherited the previous unattributed acquisition's pool provenance");
        trace.ObserveAcquisition(fixture.Pool, 0, 0xDEF, 9876, default);
        trace.CapturePool();
        var reacquired = trace.Acquisitions.Last();
        check(reacquired.Generation != acquired.Generation && reacquired.ReservationId == 8000 &&
            trace.PoolSounds.Any(item => item.ReservationId == 8000 && item.Generation == reacquired.Generation &&
                AddressIs(item.ResourceHandle, 2)) && AddressIs(assigned.ResourceHandle, 1),
            "Reacquiring the same native ID merged generations or changed an earlier diagnostic snapshot");
        trace.Stop();
    }

    private static void CheckVoices(Action<bool, string> check, Fixture fixture, nint start, nint end)
    {
        fixture.LinkVoice();
        var trace = new SfxNativeTrace(start, end, fixture.Pool); trace.Start();
        foreach (var bus in new byte[] { 0, 1, 2, 3, 4, 6, 255 })
        { fixture.Root->BusId = bus; trace.ObserveVoice(fixture.Source, fixture.Root); }
        fixture.Root->BusId = 5;
        foreach (var id in new[] { 0, -1, int.MinValue })
        { fixture.Root->SoundId = id; trace.ObserveVoice(fixture.Source, fixture.Root); }
        check(trace.Voices.Length == 0, "Unscoped voice diagnostics included another bus or a nonpositive playback ID");
        fixture.Root->SoundId = 4000; fixture.Root->ExtendedFlags = 0x84;
        trace.ObserveVoice(fixture.Source, fixture.Root); trace.ObserveVoice(fixture.Source, fixture.Root);
        var frozen = trace.Voices;
        check(frozen.Length == 1 && frozen[0].SoundId == 4000 && frozen[0].Bus == 5 && frozen[0].Flags == 0x84 &&
            AddressIs(frozen[0].Source, (nint)fixture.Source) && AddressIs(frozen[0].Callback, (nint)fixture.Owner + 8) &&
            AddressIs(frozen[0].CallbackOwner, (nint)fixture.Owner) && AddressIs(frozen[0].Track, (nint)fixture.Track) &&
            AddressIs(frozen[0].Root, (nint)fixture.Root) &&
            BitConverter.ToInt32(frozen[0].RootHeader.AsSpan().Slice(0x0C, 4)) == 4000 &&
            BitConverter.ToInt64(frozen[0].TrackHeader.AsSpan().Slice(0x10, 8)) == (long)fixture.Root,
            "A distinct bus-five playback lost its validated source/callback/track/root provenance or was duplicated");
        trace.Stop(); fixture.Root->SoundId = 4999; fixture.Root->ExtendedFlags = 0;
        trace.ObserveVoice(fixture.Source, fixture.Root); trace.Start();
        check(trace.Voices.Length == 0 && frozen[0].SoundId == 4000 && frozen[0].Flags == 0x84 &&
            BitConverter.ToInt32(frozen[0].RootHeader.AsSpan().Slice(0x0C, 4)) == 4000,
            "Stopped or reset native voice tracing mutated an earlier immutable header");
        for (var i = 0; i < 100; i++)
        {
            fixture.Root->SoundId = 5000 + i;
            trace.ObserveVoice(fixture.Source, fixture.Root); trace.ObserveVoice(fixture.Source, fixture.Root);
        }
        check(trace.Voices.Length == 64 && trace.Voices.Select(item => item.SoundId).Distinct().Count() == 64 &&
            trace.Voices.All(item => item.Bus == 5 && item.SoundId >= 5000 && item.SoundId < 5100),
            "Native voice tracing exceeded its distinct-playback limit or counted duplicate audio callbacks");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(frozen));
        check(json.RootElement[0].GetProperty("RootHeader").ValueKind == JsonValueKind.Array &&
            json.RootElement[0].GetProperty("TrackHeader").ValueKind == JsonValueKind.Array,
            "Native playback headers did not serialize as inspectable immutable JSON byte arrays");
        trace.Stop();
    }

    private static void CheckResolutions(Action<bool, string> check, Fixture fixture, nint start, nint end)
    {
        var entry = (byte*)NativeMemory.AllocZeroed(0x40);
        try
        {
            var trace = new SfxNativeTrace(start, end, fixture.Pool); trace.Start();
            *(int*)(entry + 0x38) = -12345; *(int*)(entry + 0x10) = -54321; *(nint*)(entry + 0x18) = 0x9000;
            trace.ObserveResolution(0x8000, -12344, (nint)entry);
            check(trace.Resolutions.Length == 0, "A mismatched resolution entry fabricated a reservation-to-controller link");
            trace.ObserveResolution(0x8000, -12345, (nint)entry);
            var frozen = trace.Resolutions;
            check(frozen.Length == 1 && frozen[0].ReservationId == -12345 && frozen[0].ControllerId == -54321 &&
                AddressIs(frozen[0].Layout, 0x8000) && AddressIs(frozen[0].Entry, (nint)entry) &&
                AddressIs(frozen[0].ControllerLayout, 0x9000) &&
                BitConverter.ToInt32(frozen[0].Header.AsSpan().Slice(0x38, 4)) == -12345,
                "Negative native reservation/controller IDs lost their exact sign or layout/entry provenance");
            for (var i = 0; i < 2000; i++) trace.ObserveResolution(0x8000, -12345, (nint)entry);
            Action observeResolution = () => trace.ObserveResolution(0x8000, -12345, (nint)entry);
            var allocated = AllocationChecks.MeasureAllocations(observeResolution, warmup: 0, iterations: 10000);
            check(allocated == 0 && trace.Resolutions.Length == 1,
                $"Repeated native resolution callbacks allocated {allocated} bytes or duplicated an established controller link");
            *(int*)(entry + 0x10) = -54320;
            trace.ObserveResolution(0x8000, -12345, (nint)entry);
            *(nint*)(entry + 0x18) = 0x9001;
            trace.ObserveResolution(0x8000, -12345, (nint)entry);
            trace.ObserveResolution(0x8001, -12345, (nint)entry);
            *(int*)(entry + 0x38) = -12344;
            trace.ObserveResolution(0x8001, -12344, (nint)entry);
            check(trace.Resolutions.Length == 5 && frozen[0].ControllerId == -54321 &&
                AddressIs(frozen[0].ControllerLayout, 0x9000) &&
                BitConverter.ToInt32(frozen[0].Header.AsSpan().Slice(0x10, 4)) == -54321,
                "Resolution deduplication merged a different controller/layout/reservation or mutated an old header");
            trace.Stop(); trace.Start();
            check(trace.Resolutions.Length == 0 && frozen[0].ReservationId == -12345,
                "Restarting native diagnostics retained current resolution state or changed a detached snapshot");
            for (var i = 0; i < 200; i++)
            {
                *(int*)(entry + 0x38) = -1000 - i; *(int*)(entry + 0x10) = -2000 - i;
                trace.ObserveResolution(0x8000, -1000 - i, (nint)entry);
            }
            check(trace.Resolutions.Length == 128 && trace.Resolutions.Select(item =>
                (item.Layout, item.ReservationId, item.ControllerId, item.ControllerLayout)).Distinct().Count() == 128,
                "Native controller resolution diagnostics exceeded their distinct-link capacity");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(frozen));
            check(json.RootElement[0].GetProperty("Header").ValueKind == JsonValueKind.Array,
                "Resolution entry bytes did not serialize as inspectable immutable JSON arrays");
            trace.Stop();
        }
        finally { NativeMemory.Free(entry); }
    }

    private static void CheckDisabledAndUnreadable(Action<bool, string> check, Fixture fixture, nint start, nint end)
    {
        var unreadable = VirtualAlloc(0, 0x10000, 0x3000, 0x01);
        if (unreadable == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var trace = new SfxNativeTrace(start, end, (GameAudioLayout.SoundData*)unreadable);
            foreach (var stopped in new[] { false, true })
            {
                if (stopped) { trace.Start(); trace.Stop(); }
                ExerciseDisabled(trace, unreadable, 2000);
                Action exerciseDisabled = () => ExerciseDisabledOnce(trace, unreadable);
                var allocated = AllocationChecks.MeasureAllocations(exerciseDisabled, warmup: 0, iterations: 10000);
                check(allocated == 0 && trace.TraceError == null &&
                    trace.Acquisitions.Length == 0 && trace.Voices.Length == 0 && trace.PoolSounds.Length == 0 && trace.Resolutions.Length == 0,
                    $"Disabled native diagnostics allocated {allocated} bytes, read inaccessible native memory, or collected observations");
            }
            trace.Start();
            trace.ObserveVoice((GameAudioLayout.SourceVoice*)unreadable, (GameAudioLayout.RootSound*)unreadable);
            trace.ObserveResolution(unreadable, -123, unreadable);
            trace.CapturePool();
            check(trace.Voices.Length == 0 && trace.PoolSounds.Length == 0 && trace.Resolutions.Length == 0 && !string.IsNullOrEmpty(trace.TraceError),
                "Unreadable diagnostic memory escaped safe-read isolation or became a fabricated native observation");
            trace.Stop(); trace.Start(); fixture.LinkVoice(); fixture.Root->SoundId = 6000;
            check(trace.TraceError == null, "Starting another trace retained a previous native-copy failure");
            fixture.Owner->Source = null;
            trace.ObserveVoice(fixture.Source, fixture.Root);
            fixture.Owner->Source = fixture.Source; fixture.Track->Owner = null;
            trace.ObserveVoice(fixture.Source, fixture.Root);
            fixture.Track->Owner = fixture.Root; fixture.Track->Voice = null;
            trace.ObserveVoice(fixture.Source, fixture.Root);
            check(trace.Voices.Length == 0, "Mismatched callback/track backlinks fabricated native playback provenance");
            fixture.Track->Voice = fixture.Owner;
            trace.ObserveVoice(fixture.Source, fixture.Root);
            check(trace.Voices.Length == 1 && trace.Voices[0].SoundId == 6000,
                "A failed diagnostic copy or rejected backlink disabled later valid voice observations");
            trace.Stop();
        }
        finally { if (!VirtualFree(unreadable, 0, 0x8000)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }

    private static void ExerciseDisabled(SfxNativeTrace trace, nint unreadable, int count)
    {
        for (var i = 0; i < count; i++) ExerciseDisabledOnce(trace, unreadable);
    }

    private static void ExerciseDisabledOnce(SfxNativeTrace trace, nint unreadable)
    {
        trace.ObserveAcquisition((GameAudioLayout.SoundData*)unreadable, 0, 0, 0, default);
        trace.ObserveVoice((GameAudioLayout.SourceVoice*)unreadable, (GameAudioLayout.RootSound*)unreadable);
        trace.ObserveResolution(unreadable, -123, unreadable);
        trace.CapturePool();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(nint address, nuint size, uint freeType);

    private sealed class Fixture : IDisposable
    {
        internal readonly GameAudioLayout.SoundData* Pool = (GameAudioLayout.SoundData*)NativeMemory.AllocZeroed(
            (nuint)(GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)));
        internal readonly GameAudioLayout.SourceVoice* Source = (GameAudioLayout.SourceVoice*)NativeMemory.AllocZeroed((nuint)sizeof(GameAudioLayout.SourceVoice));
        internal readonly GameAudioLayout.VoiceOwner* Owner = (GameAudioLayout.VoiceOwner*)NativeMemory.AllocZeroed((nuint)sizeof(GameAudioLayout.VoiceOwner));
        internal readonly GameAudioLayout.Track* Track = (GameAudioLayout.Track*)NativeMemory.AllocZeroed((nuint)sizeof(GameAudioLayout.Track));
        internal readonly GameAudioLayout.RootSound* Root = (GameAudioLayout.RootSound*)NativeMemory.AllocZeroed((nuint)sizeof(GameAudioLayout.RootSound));
        internal void ClearPool() => new Span<byte>(Pool,
            GameAudioLayout.PlayerSoundOwnership.Capacity * sizeof(GameAudioLayout.SoundData)).Clear();
        internal void LinkVoice()
        {
            Source->State = 3; Source->QueuedBufferCount = 1; Source->SendCount = 1;
            Source->Volume = Source->PreviousVolume = Source->Pitch = 1;
            Source->Callback = (nint)Owner + 8; Owner->Source = Source; Owner->Track = Track;
            Track->Voice = Owner; Track->Owner = Root; Root->BusId = 5;
        }
        public void Dispose()
        { NativeMemory.Free(Root); NativeMemory.Free(Track); NativeMemory.Free(Owner); NativeMemory.Free(Source); NativeMemory.Free(Pool); }
    }
}
