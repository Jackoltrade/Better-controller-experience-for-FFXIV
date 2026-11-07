// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseLighting.Core;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace DualSenseLighting.Windows;

/// <summary>One bounded segment. Native sampling stays on the framework thread; completed segments can save in the background.</summary>
internal sealed class SfxTrace
{
    private const double SegmentSeconds = 60;
    private const int MaxFrames = 1200;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    private readonly List<Frame> frames = new();
    private readonly Dictionary<(int, uint), string> names = new();
    private double nextSample;
    private string localActor = "";
    private readonly System.Collections.Generic.HashSet<string> timelineKeys = new(StringComparer.Ordinal);
    private readonly HashSet<Enemy> enemies = new();
    private readonly Dictionary<(string, string), EnemySkillFilter.EffectOwner> effectOwners = new();
    private readonly Dictionary<string, EnemySkillFilter.EffectClip> effectClips = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, EnemySkillFilter.ActionResult> actionResults = new();
    private readonly Dictionary<uint, NpcSpawnObserver.Spawn> npcSpawns = new();
    private readonly HashSet<NpcActor> npcActors = new();
    private string? spawnTraceError;
    private readonly string version;
    private readonly DateTimeOffset sessionStarted;
    private readonly int segment;
    private SfxNativeTrace.Acquisition[] nativeAcquisitions = Array.Empty<SfxNativeTrace.Acquisition>();
    private SfxNativeTrace.Voice[] environmentVoices = Array.Empty<SfxNativeTrace.Voice>();
    private SfxNativeTrace.PoolSound[] nativeSoundPool = Array.Empty<SfxNativeTrace.PoolSound>();
    private SfxNativeTrace.Resolution[] nativeResolutions = Array.Empty<SfxNativeTrace.Resolution>();
    private string? nativeTraceError;
    private readonly Dictionary<int, ArenaEffectSource> arenaSources = new();
    private string? arenaSoundError;
    public SfxTrace(string version, DateTimeOffset sessionStarted, int segment)
    { this.version = version; this.sessionStarted = sessionStarted; this.segment = segment; }
    public void Stop() => clock.Stop();
    private sealed record Sound(int Slot, int SoundId, int PlaybackId, bool Owned, uint Number, string Name,
        byte Origin, string SourceActor, string SourceDraw, int TimelineOwnerIndex, bool IsPositional, Point? Position, EnemySoundKind Enemy,
        EnemyActionTag Action, EnemyActorRole? EnemyRole, uint NameId, uint BaseId, uint OwnerId, uint BossOwnerNameId,
        uint EffectOwnerGetterRva, ImpactPoint? Impact, bool SelfTargetedExecution);
    private sealed record Enemy(string Actor, uint EntityId, int ObjectIndex, EnemySoundKind Kind,
        EnemyActorRole Role, uint NameId, uint BaseId, uint OwnerId, uint BossOwnerNameId, uint EventId, uint LayoutId);
    private sealed record NpcActor(string Actor, uint EntityId, uint NameId, uint BaseId, int ObjectIndex,
        byte SubKind, uint OwnerId, uint LayoutId, uint GimmickId, uint EventId, ushort FateId,
        string SharedGroup, string EventHandler, bool Hostile, int? TargetCategory);
    private sealed record Point(float X, float Y, float Z)
    { public static Point? Of(Vector3? point) => point.HasValue ? new(point.Value.X, point.Value.Y, point.Value.Z) : null; }
    private sealed record ImpactPoint(Point Position, uint ActionId, uint Sequence, HapticImpactSource Source, bool CasterAttached)
    {
        public static ImpactPoint? Of(HapticImpactPosition? impact) => impact is { IsValid: true } value
            ? new(Point.Of(value.Position)!, value.ActionId, value.Sequence, value.Source, value.CasterAttached) : null;
    }
    private sealed record Haptics(HapticSettings? Settings, Point? Character, Point? Camera, Point? CameraRight,
        int? ImpactSoundId, Point? ImpactPosition, long SpatialBlocks, long CutoffBlocks, long SuppressedBlocks, long BiasedBlocks,
        string? Error, long FootstepBlocks, bool FootstepContextReady, long BossBlocks, long RegularEnemyBlocks,
        HapticSettings? RequestedSettings, bool EnemySkillsReady, string? EnemySkillError,
        long EnemySkillActions, long EnemyAutoAttacks, long UnknownEnemyActions, long HitReactionBlocks,
        SfxAudioCapture.EncounterContext Encounter);
    private sealed record Frame(double Seconds, Sound[] Sounds, SfxAudioCapture.VoiceObservation[] Voices, Haptics Haptics);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(nint process, nint address, byte[] data, nuint size, out nuint read);

    private static byte[]? Read(nint address, int count)
    {
        if (address == 0) return null;
        var bytes = new byte[count];
        return ReadProcessMemory(-1, address, bytes, (nuint)count, out var read) && read == (nuint)count ? bytes : null;
    }

    private string Name(GameAudioLayout.SoundReservation sound)
    {
        var key = (sound.SoundId, sound.SoundNumber);
        if (names.TryGetValue(key, out var cached)) return cached;
        // RPM is used even in our own process: a resource can disappear between
        // snapshots, and an unreadable string must not become an unsafe dereference.
        var text = sound.ResourceHandle == 0 ? null : Read(sound.ResourceHandle + 0x48, 32);
        if (text == null) return "";
        var length = BitConverter.ToInt64(text, 16);
        var capacity = BitConverter.ToInt64(text, 24);
        if (length <= 0 || length > 1024 || capacity < length) return "";
        var bytes = capacity < 16 ? text : Read((nint)BitConverter.ToInt64(text), (int)length);
        if (bytes == null) return "";
        return names[key] = Encoding.UTF8.GetString(bytes, 0, (int)length);
    }

    private void SampleNpcActors(IObjectTable objects)
    {
        var playerPresent = objects.LocalPlayer != null;
        foreach (var item in objects)
        {
            if (npcActors.Count >= 512) break;
            if (item is not ICharacter actor || (byte)actor.ObjectKind != 2 || actor.Address == 0) continue;
            var bytes = Read(actor.Address, 0x168);
            if (bytes == null || bytes[0x90] != 2 || BitConverter.ToUInt32(bytes, 0x78) != actor.EntityId ||
                BitConverter.ToUInt16(bytes, 0x8C) != actor.ObjectIndex) continue;
            npcActors.Add(new($"{actor.Address:X}", actor.EntityId, actor.NameId, actor.BaseId, actor.ObjectIndex,
                bytes[0x91], BitConverter.ToUInt32(bytes, 0x88), BitConverter.ToUInt32(bytes, 0x7C),
                BitConverter.ToUInt32(bytes, 0x80), BitConverter.ToUInt32(bytes, 0xF4), BitConverter.ToUInt16(bytes, 0xF8),
                $"{BitConverter.ToUInt64(bytes, 0x108):X}", $"{BitConverter.ToUInt64(bytes, 0x160):X}",
                (actor.StatusFlags & StatusFlags.Hostile) != 0,
                playerPresent && actor.SubKind == (byte)BattleNpcSubKind.Combatant ? GameTargetRelation.Read(actor.Address) : null));
        }
    }

    public bool Sample(SfxAudioCapture capture, HapticSettings? requested = null, IObjectTable? objects = null, bool final = false)
    {
        var seconds = clock.Elapsed.TotalSeconds;
        var complete = seconds >= SegmentSeconds || frames.Count >= MaxFrames;
        if ((seconds >= nextSample || complete || final) && frames.Count < MaxFrames)
        {
            foreach (var arena in capture.ArenaSources)
                if (arenaSources.Count < 128 || arenaSources.ContainsKey(arena.SoundId)) arenaSources[arena.SoundId] = arena;
            arenaSoundError = capture.ArenaSoundError;
            if (capture.NativeTrace is { } native)
            {
                native.CapturePool();
                nativeAcquisitions = native.Acquisitions;
                environmentVoices = native.Voices;
                nativeSoundPool = native.PoolSounds;
                nativeResolutions = native.Resolutions;
                nativeTraceError = native.TraceError;
            }
            if (objects != null) SampleNpcActors(objects);
            foreach (var spawn in capture.NpcSpawns) npcSpawns[spawn.Metadata.EntityId] = spawn;
            spawnTraceError = capture.SpawnTraceError;
            localActor = $"{capture.PlayerAddress:X}";
            foreach (var key in capture.EnemyTimelineKeys) timelineKeys.Add(key);
            foreach (var owner in capture.EffectOwners) effectOwners[(owner.Intermediate, owner.Node)] = owner;
            // A clip can be observed before its C012 listener has initialized.
            // Retain the later resolved association rather than its first shell.
            foreach (var clip in capture.EffectClips) effectClips[clip.Clip] = clip;
            foreach (var action in capture.ActionResults) actionResults[action.Sequence] = action;
            foreach (var actor in capture.EnemyActors())
                enemies.Add(new($"{actor.Actor:X}", actor.EntityId, actor.ObjectIndex, actor.Kind, actor.Role, actor.NameId, actor.BaseId,
                    actor.OwnerId, actor.BossOwnerNameId, actor.EventId, actor.LayoutId));
            nextSample = seconds + 0.05;
            var sounds = capture.Reservations().Select(s => new Sound(s.Slot, s.SoundId, s.PlaybackId,
                s.Owned, s.SoundNumber, Name(s), s.Origin, $"{s.SourceActor:X}", $"{s.SourceDraw:X}", s.TimelineOwnerIndex,
                s.IsPositional, Point.Of(s.Position), s.Enemy, s.Action, s.EnemyRole, s.NameId, s.BaseId, s.OwnerId, s.BossOwnerNameId,
                s.EffectOwnerGetterRva, ImpactPoint.Of(s.Impact), s.SelfTargetedExecution)).ToArray();
            var output = capture.Haptics; var pose = output?.Listener; var cue = output?.ImpactCue;
            frames.Add(new Frame(seconds, sounds, capture.Voices(), new Haptics(output?.CurrentSettings,
                Point.Of(pose?.Origin), Point.Of(pose?.CameraPosition), Point.Of(pose?.CameraRight), cue?.SoundId,
                Point.Of(cue?.Position), output?.SpatialBlocks ?? 0, output?.CutoffBlocks ?? 0,
                output?.SuppressedBlocks ?? 0, output?.BiasedBlocks ?? 0, capture.SpatialError ?? capture.HapticsError,
                output?.FootstepBlocks ?? 0, output?.FootstepContextReady ?? false,
                output?.BossBlocks ?? 0, output?.RegularEnemyBlocks ?? 0,
                requested, capture.EnemySkillsReady, capture.EnemySkillError,
                capture.EnemySkillActions, capture.EnemyAutoAttacks, capture.UnknownEnemyActions, output?.HitReactionBlocks ?? 0,
                capture.EnemyEncounter)));
        }
        return complete || frames.Count >= MaxFrames;
    }

    public void Save(string path)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        TraceArchive.Save(path, started, JsonSerializer.Serialize(new { PluginVersion = version, StartedUtc = started,
            SessionStartedUtc = sessionStarted, Segment = segment,
            DurationSeconds = clock.Elapsed.TotalSeconds, LocalActor = localActor,
            EnemyActors = enemies.OrderBy(e => e.ObjectIndex).ThenBy(e => e.EntityId).ToArray(),
            NpcSpawns = npcSpawns.Values.OrderBy(e => e.Seconds).ToArray(),
            NpcActors = npcActors.OrderBy(e => e.ObjectIndex).ThenBy(e => e.EntityId).ToArray(),
            SpawnTraceError = spawnTraceError,
            NativeAcquisitions = nativeAcquisitions, EnvironmentVoices = environmentVoices,
            NativeSoundPool = nativeSoundPool, NativeTraceError = nativeTraceError,
            NativeResolutions = nativeResolutions,
            ArenaSources = arenaSources.Values.Select(source => new { source.SoundId,
                Position = Point.Of(source.Position), source.Resource, source.SoundNumber, source.PoolSlot,
                source.SoundData, source.SoundLayout, source.InstanceId, source.SubId }).ToArray(),
            ArenaSoundError = arenaSoundError,
            EffectOwners = effectOwners.Values.ToArray(),
            EffectClips = effectClips.Values.ToArray(),
            ActionResults = actionResults.Values.OrderBy(a => a.Seconds).Select(a => new
            {
                a.Seconds, a.Caster, a.CasterEntityId, a.ActionId, a.Sequence, a.AnimationTargetId, a.Targets, a.AreaTarget,
                NativeTargetPosition = Point.Of(a.NativeTargetPosition), Impact = ImpactPoint.Of(a.Impact)
            }).ToArray(),
            EnemyTimelineKeys = timelineKeys.OrderBy(k => k).ToArray(), Frames = frames }, options));
    }
}
