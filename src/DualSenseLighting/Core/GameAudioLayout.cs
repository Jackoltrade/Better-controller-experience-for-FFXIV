// SPDX-License-Identifier: MIT
// Minimal bindings adapted from aers/FFXIVClientStructs. See LICENSE-FFXIVClientStructs.txt.
#nullable enable
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DualSenseLighting.Core;

/// <summary>Read-only subset of the pinned native mixer layout; no setters or audio calls.</summary>
public static unsafe class GameAudioLayout
{
    [StructLayout(LayoutKind.Explicit, Size = 0xA48)]
    public struct SourceVoice
    {
        [FieldOffset(0xA8)] public int State;
        [FieldOffset(0xB0)] public nint Callback;
        [FieldOffset(0xD8)] public int QueuedBufferCount;
        [FieldOffset(0x530)] public float Volume;
        [FieldOffset(0x534)] public float Pitch;
        [FieldOffset(0x938)] public float PreviousVolume;
        [FieldOffset(0x990)] public int SendCount;
        [FieldOffset(0xA40)] public uint OutputFormat;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x10)]
    public struct VoiceSend
    {
        [FieldOffset(0)] public nint Destination;
        [FieldOffset(8)] public float Gain;
    }

    public static bool SendsTo(SourceVoice* source, nint destination) => TryGetSendGain(source, destination, out _);

    public static bool TryGetSendGain(SourceVoice* source, nint destination, out float gain)
    {
        gain = 0;
        if (source == null || destination == 0 || source->SendCount is < 1 or > 5) return false;
        var sends = (VoiceSend*)((byte*)source + 0x940);
        for (var i = 0; i < source->SendCount; i++)
            if (sends[i].Destination == destination) { gain = sends[i].Gain; return true; }
        return false;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x328)]
    public struct VoiceOwner
    {
        [FieldOffset(0x08)] public nint CallbackVTable;
        [FieldOffset(0x10)] public Track* Track;
        [FieldOffset(0x20)] public SourceVoice* Source;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x2F0)]
    public struct Track
    {
        [FieldOffset(0x08)] public VoiceOwner* Voice;
        [FieldOffset(0x10)] public RootSound* Owner;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x480)]
    public struct RootSound
    {
        [FieldOffset(0x0C)] public int SoundId;
        [FieldOffset(0x19)] public byte BusId;
        [FieldOffset(0x30)] public uint ExtendedFlags;
        // Volume routing is not caster identity; shared hit sounds can use category zero.
        [FieldOffset(0x450)] public int PlayerCategory;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0xD0)]
    public struct SoundData
    {
        [FieldOffset(0x24)] public byte IsActive;
        [FieldOffset(0x28)] public nint ResourceHandle;
        [FieldOffset(0x40)] public int SoundId;
        [FieldOffset(0x68)] public Vector3 Position;
        [FieldOffset(0x7C)] public uint SoundNumber;
        [FieldOffset(0xB9)] public byte IsPositional;
    }

    public readonly record struct SoundReservation(int Slot, int SoundId, int PlaybackId, bool Owned,
        nint ResourceHandle, uint SoundNumber, byte Origin, nint SourceActor, nint SourceDraw, int TimelineOwnerIndex,
        bool IsPositional = false, Vector3? Position = null);

    public sealed record PlayerIdentity(nint Actor, uint EntityId, nint DrawObject)
    {
        private nint[] drawChildren = Array.Empty<nint>();
        public void SetDrawChildren(ReadOnlySpan<nint> children)
        {
            if (!children.SequenceEqual(Volatile.Read(ref drawChildren))) Volatile.Write(ref drawChildren, children.ToArray());
        }
        // A listener's first member is only a pointer lookup, never an object to dereference.
        public bool Owns(nint caster, nint drawObject) => Actor != 0 &&
            (caster != 0 ? caster == Actor : drawObject != 0 &&
                (drawObject == DrawObject || ((ReadOnlySpan<nint>)Volatile.Read(ref drawChildren)).Contains(drawObject)));
    }

    /// <summary>One ownership tag per native pool slot, cleared on every acquisition.</summary>
    public sealed class PlayerSoundOwnership
    {
        public const int Capacity = 256;
        private readonly SoundData* pool;
        private readonly PlayerIdentity?[] owners = new PlayerIdentity?[Capacity];
        private readonly long[] playbackLinks = new long[Capacity];
        private readonly byte[] origins = new byte[Capacity];
        private readonly nint[] sourceActors = new nint[Capacity], sourceDraws = new nint[Capacity];
        private readonly int[] timelineOwners = new int[Capacity];
        private PlayerIdentity player = new(0, 0, 0);

        public PlayerSoundOwnership(SoundData* pool) => this.pool = pool;
        public PlayerIdentity Player => Volatile.Read(ref player);

        // Called on the framework thread. A new identity invalidates every old slot atomically,
        // even across logout/login when the game reuses an actor address.
        public bool SetPlayer(nint actor, uint entityId, nint drawObject, ReadOnlySpan<nint> drawChildren = default)
        {
            var current = Player;
            if (current.Actor == actor && current.EntityId == entityId && current.DrawObject == drawObject)
            { current.SetDrawChildren(drawChildren); return false; }
            var next = new PlayerIdentity(actor, entityId, drawObject);
            next.SetDrawChildren(drawChildren);
            Volatile.Write(ref player, next);
            return true;
        }

        public void Acquired(SoundData* sound, PlayerIdentity? caster, byte origin = 0, nint sourceActor = 0, nint sourceDraw = 0,
            int timelineOwnerIndex = -1)
        {
            var offset = (nuint)sound - (nuint)pool;
            if (pool == null || offset >= (nuint)(Capacity * sizeof(SoundData)) || offset % (nuint)sizeof(SoundData) != 0) return;
            // Unknown/other callers must clear the old owner too. The native SoundId can
            // arrive later during asynchronous loading, so record the reservation itself.
            var slot = (int)(offset / (nuint)sizeof(SoundData));
            var own = ReferenceEquals(caster, Player) && caster!.Actor != 0;
            Volatile.Write(ref owners[slot], null);
            Volatile.Write(ref playbackLinks[slot], 0);
            origins[slot] = origin; sourceActors[slot] = sourceActor; sourceDraws[slot] = sourceDraw;
            timelineOwners[slot] = timelineOwnerIndex;
            Volatile.Write(ref owners[slot], own ? caster : null);
        }

        public bool Contains(int soundId) => FindOwned(soundId) != null;

        public bool TryGetPosition(int soundId, out Vector3 position)
        {
            var sound = FindOwned(soundId);
            position = sound == null ? default : sound->Position;
            return sound != null && sound->IsPositional == 1 && HapticSpatializer.ValidPosition(position);
        }

        private SoundData* FindOwned(int soundId)
        {
            if (pool == null || soundId <= 0) return null;
            var current = Player;
            if (current.Actor == 0) return null;
            for (var slot = 0; slot < Capacity; slot++)
            {
                if (!ReferenceEquals(Volatile.Read(ref owners[slot]), current)) continue;
                var sound = pool + slot;
                var reservationId = sound->SoundId;
                var link = Volatile.Read(ref playbackLinks[slot]);
                var matches = reservationId == soundId ||
                    (link != 0 && (int)(link >> 32) == reservationId && (int)link == soundId);
                if (sound->IsActive != 0 && matches &&
                    ReferenceEquals(Volatile.Read(ref owners[slot]), current) && ReferenceEquals(Player, current)) return sound;
            }
            return null;
        }

        public bool LinkPlayback(int reservationId, int playbackId)
        {
            // Positional sounds have a negative layout handle; its embedded
            // controller gets a separate positive mixer ID after loading/playback.
            if (pool == null || reservationId >= -1 || playbackId <= 0) return false;
            var current = Player;
            if (current.Actor == 0) return false;
            for (var slot = 0; slot < Capacity; slot++)
            {
                var sound = pool + slot;
                if (sound->IsActive == 0 || sound->SoundId != reservationId) continue;
                var pair = (long)(((ulong)(uint)reservationId << 32) | (uint)playbackId);
                Volatile.Write(ref playbackLinks[slot], pair);
                // Keep unowned links for diagnostics too; Contains still requires
                // independent caster ownership before any voice affects lighting.
                return ReferenceEquals(Volatile.Read(ref owners[slot]), current) && ReferenceEquals(Player, current);
            }
            return false;
        }

        // Diagnostic snapshots run on the framework thread, never the mixer thread.
        public SoundReservation[] Snapshot()
        {
            if (pool == null) return Array.Empty<SoundReservation>();
            var result = new SoundReservation[Capacity];
            var count = 0;
            var current = Player;
            for (var slot = 0; slot < Capacity; slot++)
            {
                var sound = pool + slot;
                if (sound->IsActive == 0) continue;
                var id = sound->SoundId;
                var pair = Volatile.Read(ref playbackLinks[slot]);
                result[count++] = new SoundReservation(slot, id, pair != 0 && (int)(pair >> 32) == id ? (int)pair :
                    id > 0 ? id : 0, ReferenceEquals(Volatile.Read(ref owners[slot]), current), sound->ResourceHandle, sound->SoundNumber,
                    origins[slot], sourceActors[slot], sourceDraws[slot], timelineOwners[slot], sound->IsPositional == 1,
                    HapticSpatializer.ValidPosition(sound->Position) ? sound->Position : null);
            }
            Array.Resize(ref result, count);
            return result;
        }
    }

    public static bool IsCombatSound(byte busId, uint flags) => (busId is 2 or 10) && (flags & 4) == 0;

    public static int SampleCount(int frames, int channels, int spatialAudio, uint format)
    {
        // The prototype supports the validated normal and ambisonic layouts only.
        if (frames != 256 || channels != 8 || spatialAudio is not (0 or 1) || format > 1) return 0;
        return frames * (format == 1 && spatialAudio == 1 ? 16 : channels);
    }

    public static bool IsCombatVoice(SourceVoice* source, nint callbackVTable, SfxSource scope = SfxSource.AllCombat,
        PlayerSoundOwnership? ownership = null)
    {
        var sound = SoundOfVoice(source, callbackVTable);
        return sound != null && IsCombatSound(sound->BusId, sound->ExtendedFlags) &&
            (scope == SfxSource.AllCombat || (scope == SfxSource.PlayerOnly && ownership?.Contains(sound->SoundId) == true));
    }

    public static RootSound* SoundOfVoice(SourceVoice* source, nint callbackVTable)
    {
        if (source == null || callbackVTable == 0 || source->State != 3 || source->QueuedBufferCount is < 1 or > 2 ||
            source->SendCount is < 1 or > 5 || !float.IsFinite(source->Pitch) || source->Pitch <= 0 ||
            !float.IsFinite(source->Volume) || !float.IsFinite(source->PreviousVolume) ||
            (source->Volume <= 0.0001f && source->PreviousVolume <= 0.0001f)) return null;
        // The game itself calls this callback interface. Check its exact vtable before
        // treating it as a VoiceSead owner, then validate both links back to this voice.
        var callback = source->Callback;
        if (callback == 0 || *(nint*)callback != callbackVTable) return null;
        var owner = (VoiceOwner*)(callback - 8);
        if (owner->Source != source || owner->Track == null || owner->Track->Voice != owner) return null;
        return owner->Track->Owner;
    }
}
