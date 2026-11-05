// SPDX-License-Identifier: MIT
// Minimal bindings adapted from aers/FFXIVClientStructs. See LICENSE-FFXIVClientStructs.txt.
#nullable enable
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DualSenseLighting.Core;

public enum EnemyActorRole { Combatant, BossMechanic, BossSummon, Unclassified, BossEncounter }

/// <summary>Read-only subset of the pinned native mixer layout; no setters or audio calls.</summary>
public static unsafe class GameAudioLayout
{
    // Sound-only boss effect actors in the pinned client; not named by Dalamud's enum.
    public const byte BossMechanicSubKind = 11;
    // The scene VFX constructor initializes its Apricot listener at +0x90.
    public const string SceneVfxConstructorSignature = "48 89 5C 24 08 57 48 83 EC 20 48 8B F9 E8 ?? ?? ?? ?? 48 8D 8F 90 00 00 00 E8 ?? ?? ?? ?? 48 8B 5C 24 30 48 8D 05 ?? ?? ?? ?? 48 89 07 48 8D 05 ?? ?? ?? ?? 48 89 87 90 00 00 00";
    public static bool IsEffectClip(nint clip)
    {
        if (clip == 0 || *(uint*)(clip + 0x84) != 9) return false;
        var data = *(nint*)(clip + 0x48);
        return data != 0 && *(uint*)(data - 8) == 0x32313043 && *(uint*)(data - 4) == 0x48;
    }

    public static nint ClipEffectListener(nint clip, nint moduleStart, nint moduleEnd, nint sceneTable, nint listenerTable,
        nint searchBase = 0)
    {
        if (!IsEffectClip(clip) || sceneTable == 0 || listenerTable == 0) return 0;
        var clipTable = *(nint*)clip;
        if (clipTable < moduleStart || clipTable > moduleEnd - 52 * 8) return 0;
        var update = InspectionAddress(*(nint*)(clipTable + 51 * 8), moduleStart, searchBase == 0 ? moduleStart : searchBase,
            (int)(moduleEnd - moduleStart), 26);
        ReadOnlySpan<byte> layout = [0x40, 0x53, 0x56, 0x57, 0x41, 0x54, 0x48, 0x83, 0xec, 0x78, 0x4c, 0x8b, 0x61,
            0x30, 0x33, 0xf6, 0x48, 0x8b, 0xb9, 0x98, 0, 0, 0, 0x48, 0x8b, 0xd9];
        if (update == 0 || !new ReadOnlySpan<byte>((void*)update, 26).SequenceEqual(layout)) return 0;
        // C012 -> native effect container -> scene handler. Read only the concrete
        // getter validated by the pinned client; never call an unknown vfunc.
        var container = *(nint*)(clip + 0x98);
        var handler = container == 0 ? 0 : *(nint*)(container + 0x28);
        var table = handler == 0 ? 0 : *(nint*)handler;
        if (table < moduleStart || table > moduleEnd - 14 * 8) return 0;
        var getter = *(nint*)(table + 13 * 8);
        if (getter < moduleStart || getter > moduleEnd - 8 || *(ulong*)getter != 0xC300000080818B48UL) return 0;
        var effect = *(nint*)(handler + 0x80);
        return effect != 0 && *(nint*)effect == sceneTable && *(nint*)(effect + 0x90) == listenerTable
            ? effect + 0x90 : 0;
    }
    public const string SchedulerActorSignature = "40 53 48 83 EC 20 48 8B D9 48 8D 05 ?? ?? ?? ?? 33 C9 80 63 11 80 48 89 4B 08";
    public const string ClipPositionSignature = "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 41 56 41 57 48 83 EC 20 41 C7 00 06 00 00 00";

    public static bool ClipPositionLayout(ReadOnlySpan<byte> body) => body.Length >= 0x110 &&
        body.IndexOf(new byte[] { 0x83, 0xf8, 0x34 }) >= 0 &&
        body.IndexOf(new byte[] { 0x48, 0x8b, 0x69, 0x48 }) >= 0 &&
        body.IndexOf(new byte[] { 0xf6, 0x45, 0x14, 0x01 }) >= 0 &&
        body.IndexOf(new byte[] { 0xf6, 0x45, 0x14, 0x04 }) >= 0 &&
        body.IndexOf(new byte[] { 0x0f, 0xb6, 0x55, 0x15 }) >= 0;

    public static bool ActorBindingLayout(ReadOnlySpan<byte> body) => body.Length >= 0x50 &&
        body.StartsWith(new byte[] { 0x40, 0x53, 0x48, 0x83, 0xec, 0x20, 0x48, 0x8b, 0xd9, 0x48, 0x8d, 0x05 }) &&
        body.IndexOf(new byte[] { 0x44, 0x89, 0x43, 0x50, 0x44, 0x89, 0x4b, 0x54 }) >= 0 &&
        body.IndexOf(new byte[] { 0xc7, 0x43, 0x58, 0x00, 0x00, 0xff, 0x01 }) >= 0;

    public static EnemySoundBinding ClipSoundBinding(nint clip, nint actorVtable, bool casterOriginSupported = false)
    {
        if (clip == 0 || actorVtable == 0 || *(uint*)(clip + 0x84) != 0x34) return EnemySoundBinding.Unknown;
        // BaseClip.Data is the C063 body (after its eight-byte entry header).
        // C053 voice/character clips never qualify through this path.
        var data = *(nint*)(clip + 0x48);
        if (data == 0 || *(uint*)(data - 8) != 0x33363043 || *(uint*)(data - 4) != 0x20) return EnemySoundBinding.Unknown;
        // Reuse the same cached clip -> actor link as the native timeline resolver.
        var actor = *(nint*)(clip + 0x28);
        if (actor == 0)
        {
            var track = *(nint*)(clip + 8);
            if (track != 0) actor = *(nint*)(track + 8);
        }
        if (actor == 0 || *(nint*)actor != actorVtable) return EnemySoundBinding.Unknown;
        return *(uint*)(actor + 0x50) switch
        {
            0 => EnemySoundBinding.Default,
            // Native flags 0x01 use positional actor origin. 0x04 selects a
            // skeletal bind point, and 0x02 stops on animation end. Admit only
            // the validated duration-one/default positional form; do not infer
            // semantic impact timing or inspect SCD filenames here.
            1 => casterOriginSupported && *(int*)(data + 4) == 1 && *(byte*)(data + 0x14) == 0x01
                ? EnemySoundBinding.CasterOrigin : EnemySoundBinding.Caster,
            2 => EnemySoundBinding.Target,
            _ => EnemySoundBinding.Unknown
        };
    }
    public const string ActionEffectSignature = "E8 ?? ?? ?? ?? 48 8B 8D ?? ?? ?? ?? 48 33 CC E8 ?? ?? ?? ?? 48 81 C4 00 05 00 00";

    // SigScanner returns live addresses, but its SearchBase can hold the original
    // module copy. Validate that copy: legitimate hooks replace live prologues.
    public static nint InspectionAddress(nint address, nint module, nint search, int size, int length)
    {
        var offset = address - module;
        return module != 0 && search != 0 && length > 0 && size >= length && offset >= 0 && offset <= size - length
            ? search + offset : 0;
    }

    public static bool ActionEffectLayout(ReadOnlySpan<byte> body)
    {
        ReadOnlySpan<byte> prologue = [0x40, 0x55, 0x53, 0x56, 0x57, 0x41, 0x54];
        ReadOnlySpan<byte> headerRead = [0x41, 0x0f, 0xb7, 0x49, 0x1c];
        return body.Length >= 0x90 && body.StartsWith(prologue) && body.IndexOf(headerRead) >= 0;
    }
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
        bool IsPositional = false, Vector3? Position = null, EnemySoundKind Enemy = EnemySoundKind.None,
        EnemyActionTag Action = default, EnemyActorRole? EnemyRole = null, uint NameId = 0, uint BaseId = 0,
        uint OwnerId = 0, uint BossOwnerNameId = 0, uint EffectOwnerGetterRva = 0);

    public readonly record struct EnemyActor(nint Actor, uint EntityId, nint DrawObject, int ObjectIndex, EnemySoundKind Kind,
        EnemyActorRole Role = EnemyActorRole.Combatant, uint NameId = 0, uint BaseId = 0,
        uint OwnerId = 0, uint BossOwnerNameId = 0, uint EventId = 0, uint LayoutId = 0);
    public readonly record struct VoiceOwnership(bool Owned, bool HitReaction, EnemySoundKind Enemy, Vector3? Position);
    private sealed record EnemyIdentity(EnemyActor Actor, PlayerIdentity Identity);

    public static int SelectEnemyActors(ReadOnlySpan<EnemyActor> candidates, Span<EnemyActor> selected)
    {
        var count = 0;
        foreach (var candidate in candidates)
        {
            var actor = candidate;
            if (count == selected.Length) break;
            if (actor.Actor == 0 || actor.EntityId is 0 or 0xE0000000 || actor.Kind == EnemySoundKind.None) continue;
            // The caller assigns BossEncounter only after validating the live
            // instance, NPC provenance and the actor's sticky classification.
            var allowed = actor.Role == EnemyActorRole.Combatant ||
                actor.Role == EnemyActorRole.BossEncounter && actor.Kind == EnemySoundKind.Boss;
            if (actor.Role == EnemyActorRole.BossMechanic && actor.Kind == EnemySoundKind.Boss && actor.NameId != 0 && actor.BaseId != 0)
                foreach (var root in candidates)
                    if (root.Role == EnemyActorRole.Combatant && root.Kind == EnemySoundKind.Boss && root.Actor != 0 &&
                        root.EntityId is not (0 or 0xE0000000) && root.NameId == actor.NameId)
                    { allowed = true; break; }
            if (actor.Role != EnemyActorRole.BossEncounter &&
                (BossMechanics.HasOwner(actor.OwnerId) || actor.BossOwnerNameId != 0))
                foreach (var root in candidates)
                    if (root.Role == EnemyActorRole.Combatant && root.Kind == EnemySoundKind.Boss && root.Actor != 0 &&
                        root.EntityId is not (0 or 0xE0000000) && root.Actor != actor.Actor &&
                        (BossMechanics.HasOwner(actor.OwnerId) ? actor.OwnerId == root.EntityId : actor.BossOwnerNameId == root.NameId))
                    {
                        actor = actor with { Kind = EnemySoundKind.Boss, Role = EnemyActorRole.BossSummon };
                        allowed = true; break;
                    }
            if (allowed) selected[count++] = actor;
        }
        return count;
    }

    public sealed record PlayerIdentity(nint Actor, uint EntityId, nint DrawObject)
    {
        public const int ActionCapacity = 64;
        private readonly int[] observedActions = new int[ActionCapacity];
        public uint ObservedAction(int index) => (uint)Volatile.Read(ref observedActions[index]);
        public void ObserveAction(uint id)
        {
            if (id == 0 || id > int.MaxValue) return;
            for (var i = 0; i < observedActions.Length; i++)
            {
                var before = Volatile.Read(ref observedActions[i]);
                if (before == (int)id || before == 0 && Interlocked.CompareExchange(ref observedActions[i], (int)id, 0) == 0) return;
            }
        }
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
        private readonly int[] playbackGenerations = new int[Capacity];
        private readonly byte[] origins = new byte[Capacity];
        private readonly uint[] effectOwnerGetters = new uint[Capacity];
        private readonly nint[] sourceActors = new nint[Capacity], sourceDraws = new nint[Capacity];
        private readonly int[] timelineOwners = new int[Capacity];
        private readonly EnemyActionTag[] enemyActions = new EnemyActionTag[Capacity];
        private readonly Func<nint, bool?>? targetingCue;
        private readonly int[] cueGenerations = new int[Capacity], cueLocks = new int[Capacity];
        private readonly CueCache[] cueCache = new CueCache[Capacity];
        private struct CueCache
        {
            public int Generation, SoundId;
            public nint Handle;
            public uint Number;
            public bool Valid, IsCue;
        }
        private PlayerIdentity player = new(0, 0, 0);
        private EnemyIdentity[] enemies = Array.Empty<EnemyIdentity>();
        private uint enemyScene;

        public PlayerSoundOwnership(SoundData* pool, Func<nint, bool?>? targetingCue = null)
        { this.pool = pool; this.targetingCue = targetingCue; }
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
            Volatile.Write(ref enemies, Array.Empty<EnemyIdentity>());
            return true;
        }

        // Framework-only publication. Reuse identities while an actor stays alive;
        // removing/replacing an actor invalidates its queued voices, including address reuse.
        public void SetEnemies(ReadOnlySpan<EnemyActor> actors, uint scene)
        {
            var current = Volatile.Read(ref enemies);
            if (enemyScene != scene) { current = Array.Empty<EnemyIdentity>(); enemyScene = scene; }
            var same = current.Length == actors.Length;
            for (var i = 0; same && i < actors.Length; i++) same = current[i].Actor == actors[i];
            if (same && ReferenceEquals(current, Volatile.Read(ref enemies))) return;
            var next = actors.Length == 0 ? Array.Empty<EnemyIdentity>() : new EnemyIdentity[actors.Length];
            for (var i = 0; i < actors.Length; i++)
            {
                var actor = actors[i];
                EnemyIdentity? existing = null;
                foreach (var entry in current) if (entry.Actor == actor) { existing = entry; break; }
                next[i] = existing ?? new EnemyIdentity(actor, new PlayerIdentity(actor.Actor, actor.EntityId, actor.DrawObject));
            }
            Volatile.Write(ref enemies, next);
        }

        public PlayerIdentity? ResolveCaster(nint actor, nint drawObject)
        {
            var local = Player;
            if (local.Owns(actor, drawObject)) return local;
            foreach (var enemy in Volatile.Read(ref enemies))
                if (enemy.Identity.Owns(actor, drawObject)) return enemy.Identity;
            return null;
        }

        public PlayerIdentity? ResolveTimelineCaster(int index, int localIndex)
        {
            if (index < 0) return null;
            var local = Player;
            if (local.Actor != 0 && index == localIndex) return local;
            foreach (var enemy in Volatile.Read(ref enemies))
                if (enemy.Actor.ObjectIndex == index) return enemy.Identity;
            return null;
        }

        private EnemySoundKind KindOf(PlayerIdentity? identity) => KindOf(identity, Volatile.Read(ref enemies));

        private static EnemySoundKind KindOf(PlayerIdentity? identity, EnemyIdentity[] roster)
            => KindOf(identity, roster, out _);

        private static EnemySoundKind KindOf(PlayerIdentity? identity, EnemyIdentity[] roster, out EnemyActorRole role)
        {
            role = default;
            if (identity == null || identity.Actor == 0) return EnemySoundKind.None;
            foreach (var enemy in roster)
                if (ReferenceEquals(enemy.Identity, identity)) { role = enemy.Actor.Role; return enemy.Actor.Kind; }
            return EnemySoundKind.None;
        }

        public EnemyActor? ActorOf(PlayerIdentity? identity)
        {
            foreach (var enemy in Volatile.Read(ref enemies))
                if (ReferenceEquals(enemy.Identity, identity)) return enemy.Actor;
            return null;
        }

        // Framework-only diagnostic snapshot; native sound callbacks use ActorOf instead.
        public EnemyActor[] EnemyActors()
        {
            var roster = Volatile.Read(ref enemies);
            var result = new EnemyActor[roster.Length];
            for (var i = 0; i < roster.Length; i++) result[i] = roster[i].Actor;
            return result;
        }

        public void Acquired(SoundData* sound, PlayerIdentity? caster, byte origin = 0, nint sourceActor = 0, nint sourceDraw = 0,
            int timelineOwnerIndex = -1, EnemyActionTag action = default, uint effectOwnerGetterRva = 0)
        {
            var offset = (nuint)sound - (nuint)pool;
            if (pool == null || offset >= (nuint)(Capacity * sizeof(SoundData)) || offset % (nuint)sizeof(SoundData) != 0) return;
            // Unknown/other callers must clear the old owner too. The native SoundId can
            // arrive later during asynchronous loading, so record the reservation itself.
            var slot = (int)(offset / (nuint)sizeof(SoundData));
            var own = caster != null && (ReferenceEquals(caster, Player) && caster.Actor != 0 || KindOf(caster) != EnemySoundKind.None);
            Volatile.Write(ref owners[slot], null);
            // Invalidate even if the game reuses every native identity field.
            Interlocked.Increment(ref cueGenerations[slot]);
            Volatile.Write(ref playbackGenerations[slot], 0);
            Volatile.Write(ref playbackLinks[slot], 0);
            origins[slot] = origin; sourceActors[slot] = sourceActor; sourceDraws[slot] = sourceDraw;
            timelineOwners[slot] = timelineOwnerIndex;
            effectOwnerGetters[slot] = effectOwnerGetterRva;
            enemyActions[slot] = action;
            Volatile.Write(ref owners[slot], own ? caster : null);
        }

        public bool Contains(int soundId) => Inspect(soundId, false, false).Owned;

        public bool IsPlayerHitReaction(int soundId) => Inspect(soundId, false, false).HitReaction;

        public static bool HitsPlayer(uint playerId, ReadOnlySpan<ulong> targets, ReadOnlySpan<byte> effects)
        {
            if (playerId is 0 or 0xE0000000 || targets.Length is 0 or > 32 || effects.Length != targets.Length * 64) return false;
            for (var target = 0; target < targets.Length; target++)
            {
                if ((uint)targets[target] != playerId) continue;
                for (var effect = 0; effect < 8; effect++)
                    if (effects[target * 64 + effect * 8] is 3 or 5 or 6) return true;
            }
            return false;
        }

        public EnemySoundKind EnemyKindOf(int soundId) => Inspect(soundId, true, false).Enemy;

        public bool TryGetEnemyPosition(int soundId, out Vector3 position)
        {
            var observation = Inspect(soundId, true, true);
            position = observation.Enemy != EnemySoundKind.None ? observation.Position ?? default : default;
            return observation.Enemy != EnemySoundKind.None && observation.Position.HasValue;
        }

        // Copy the complete attribution before validating its generation. Never
        // return a live native slot for callers to read after it can be reused.
        // One mixer voice shares this value across ownership, hit and position
        // decisions. Lighting-only callers skip enemy and positional inspection.
        public VoiceOwnership Inspect(int soundId, bool includeEnemies, bool includePosition)
        {
            var local = Player;
            var roster = Volatile.Read(ref enemies);
            if (pool == null || soundId <= 0 || local.Actor == 0) return default;
            for (var slot = 0; slot < Capacity; slot++)
            {
                // These are only cheap candidate filters. Capture the lifetime
                // afterward, then revalidate every field used for attribution.
                if (!includeEnemies && !ReferenceEquals(Volatile.Read(ref owners[slot]), local)) continue;
                var sound = pool + slot;
                var reservation = sound->SoundId;
                var linked = reservation != soundId;
                var link = linked ? Volatile.Read(ref playbackLinks[slot]) : 0;
                if (sound->IsActive == 0 || linked &&
                    (link == 0 || (int)(link >> 32) != reservation || (int)link != soundId)) continue;
                var generation = Volatile.Read(ref cueGenerations[slot]);
                var owner = Volatile.Read(ref owners[slot]);
                if (owner == null) continue;
                var owned = ReferenceEquals(owner, local);
                if (!owned && (!includeEnemies || roster.Length == 0)) continue;
                var linkGeneration = linked ? Volatile.Read(ref playbackGenerations[slot]) : 0;
                if (linked && linkGeneration != generation) continue;
                var handle = sound->ResourceHandle;
                var number = sound->SoundNumber;
                var action = enemyActions[slot];
                var positional = sound->IsPositional;
                var position = includePosition && positional == 1 ? sound->Position : default;
                var role = default(EnemyActorRole);
                var kind = owned ? EnemySoundKind.None : KindOf(owner, roster, out role);
                var encounterNpc = kind == EnemySoundKind.Boss && role == EnemyActorRole.BossEncounter;
                if (!owned && (kind == EnemySoundKind.None ||
                    !encounterNpc && (!action.IsSkill || kind == EnemySoundKind.Boss && !action.IsReleasedEffect ||
                        IsTargetingCue(slot, sound, owner, generation, reservation)))) continue;
                if (sound->IsActive == 0 || sound->SoundId != reservation || sound->ResourceHandle != handle ||
                    sound->SoundNumber != number || includePosition && sound->IsPositional != positional ||
                    generation != Volatile.Read(ref cueGenerations[slot]) ||
                    linked && (link != Volatile.Read(ref playbackLinks[slot]) || linkGeneration != Volatile.Read(ref playbackGenerations[slot])) ||
                    !ReferenceEquals(owner, Volatile.Read(ref owners[slot])) || !ReferenceEquals(local, Player) ||
                    !owned && !ReferenceEquals(roster, Volatile.Read(ref enemies))) continue;
                return new(owned, owned && action.IsHitReaction, kind,
                    includePosition && positional == 1 && HapticSpatializer.ValidPosition(position) ? position : null);
            }
            return default;
        }

        private bool IsTargetingCue(int slot, SoundData* sound, PlayerIdentity? owner, int generation, int soundId)
        {
            var handle = sound->ResourceHandle;
            if (targetingCue == null || handle == 0) return false;
            // A concurrent classifier must not leak the first marker block. Skip
            // only this added enemy feed; native playback is never affected.
            if (Interlocked.CompareExchange(ref cueLocks[slot], 1, 0) != 0) return true;
            try
            {
                var number = sound->SoundNumber;
                ref var cached = ref cueCache[slot];
                if (cached.Valid && cached.Generation == generation && cached.SoundId == soundId &&
                    cached.Handle == handle && cached.Number == number) return cached.IsCue;
                var result = targetingCue(handle);
                if (!result.HasValue || sound->IsActive == 0 || sound->SoundId != soundId ||
                    sound->ResourceHandle != handle || sound->SoundNumber != number ||
                    Volatile.Read(ref cueGenerations[slot]) != generation ||
                    !ReferenceEquals(owner, Volatile.Read(ref owners[slot]))) return false;
                cached = new CueCache { Valid = true, Generation = generation, SoundId = soundId,
                    Handle = handle, Number = number, IsCue = result.Value };
                return result.Value;
            }
            // An optional name read must never disrupt ownership or native audio.
            catch { return false; }
            finally { Volatile.Write(ref cueLocks[slot], 0); }
        }

        public bool TryGetPosition(int soundId, out Vector3 position)
        {
            var observation = Inspect(soundId, false, true);
            position = observation.Position ?? default;
            return observation.Position.HasValue;
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
                var generation = Volatile.Read(ref cueGenerations[slot]);
                if (sound->IsActive == 0 || sound->SoundId != reservationId) continue;
                var pair = (long)(((ulong)(uint)reservationId << 32) | (uint)playbackId);
                Volatile.Write(ref playbackLinks[slot], pair);
                Volatile.Write(ref playbackGenerations[slot], generation);
                if (sound->IsActive == 0 || sound->SoundId != reservationId ||
                    generation != Volatile.Read(ref cueGenerations[slot]))
                {
                    Interlocked.CompareExchange(ref playbackGenerations[slot], 0, generation);
                    return false;
                }
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
                var enemy = ActorOf(Volatile.Read(ref owners[slot]));
                result[count++] = new SoundReservation(slot, id, pair != 0 &&
                    Volatile.Read(ref playbackGenerations[slot]) == Volatile.Read(ref cueGenerations[slot]) &&
                    (int)(pair >> 32) == id ? (int)pair :
                    id > 0 ? id : 0, ReferenceEquals(Volatile.Read(ref owners[slot]), current), sound->ResourceHandle, sound->SoundNumber,
                    origins[slot], sourceActors[slot], sourceDraws[slot], timelineOwners[slot], sound->IsPositional == 1,
                    HapticSpatializer.ValidPosition(sound->Position) ? sound->Position : null, enemy?.Kind ?? EnemySoundKind.None,
                    enemyActions[slot], enemy?.Role, enemy?.NameId ?? 0, enemy?.BaseId ?? 0,
                    enemy?.OwnerId ?? 0, enemy?.BossOwnerNameId ?? 0, effectOwnerGetters[slot]);
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
