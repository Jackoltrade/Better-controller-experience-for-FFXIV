// SPDX-License-Identifier: GPL-3.0-only
// Native signatures/layout adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>Observes source PCM and optionally adds a separate feed to native vibration processing.</summary>
internal sealed unsafe class SfxAudioCapture : IDisposable
{
    private const string RenderSignature = "48 8B C4 48 89 58 18 55 56 57 41 54 41 55 41 56 41 57 48 8D A8 D8 F9 FF FF";
    private const string CallbackSignature = "48 8D 4F 50 48 8D 05 ?? ?? ?? ?? 48 89 5F 18 8D 53 60 48 89 47 08";
    private const string AnimationSignature = "4C 8B DC 56 48 81 EC ?? ?? ?? ?? F3 0F 10 89";
    private const string AcquireSignature = "E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 0F 84 ?? ?? ?? ?? 44 0F B6 74 24";
    private const string ResolveSignature = "48 89 5C 24 20 57 48 83 EC 20 48 8D 99 78 01 00 00 48 89 6C 24 30 48 89 74 24 38 8B F2";
    private readonly Hook<RenderDelegate> render;
    private Hook<SubmixRenderDelegate>? confirmationRender;
    private int* sampleRate;
    private Hook<AnimationSoundDelegate>? animation;
    private Hook<AcquireSoundDelegate>? acquire;
    private Hook<ResolveSoundDelegate>? resolve;
    private Hook<CharacterSoundDelegate>? characterSound;
    private Hook<ClipUpdateDelegate>? clipUpdate;
    private Hook<GetTimelineDelegate>? getTimeline;
    private GameAudioLayout.PlayerSoundOwnership? ownership;
    private EnemySkillFilter? enemySkills;
#if BCEF_DIAGNOSTICS
    private NpcSpawnObserver? npcSpawns;
    private SfxNativeTrace? nativeTrace;
#endif
    private ArenaSoundEffects? arenaSounds;
    private string? arenaSoundError;
    public string? ArenaSoundError => arenaSoundError ?? arenaSounds?.Error;
#if BCEF_DIAGNOSTICS
    public ArenaEffectSource[] ArenaSources => arenaSounds?.Sources ?? Array.Empty<ArenaEffectSource>();
#endif
    public void UpdateArena(uint territory, bool active, bool prepare = false) => arenaSounds?.Update(territory, active, prepare);
    public void ObserveEnemyCast(nint actor, uint entityId, uint actionId, byte actionType) =>
        enemySkills?.ObserveCast(actor, entityId, actionId, actionType);
#if BCEF_DIAGNOSTICS
    public SfxNativeTrace? NativeTrace => nativeTrace;
    private string? spawnTraceError;
    public string? SpawnTraceError => spawnTraceError ?? npcSpawns?.Error;
    public NpcSpawnObserver.Spawn[] NpcSpawns => npcSpawns?.Snapshot() ?? Array.Empty<NpcSpawnObserver.Spawn>();
#endif
    private string? enemySkillError;
    public string? EnemySkillError => enemySkillError ?? (enemySkills is { Ready: false }
        ? "Enemy skill observation stopped. Reload the plugin to retry." : enemySkills?.ClipBindingError ?? enemySkills?.EffectBindingError);
    public bool EnemySkillsReady => PlayerFilterSupported && enemySkills?.Ready == true;
#if BCEF_DIAGNOSTICS
    public long EnemySkillActions => enemySkills?.Skills ?? 0;
    public long EnemyAutoAttacks => enemySkills?.AutoAttacks ?? 0;
    public long UnknownEnemyActions => enemySkills?.UnknownActions ?? 0;
    public readonly record struct EncounterContext(uint Duty, byte QueueMaxPlayers, byte MembersPerParty,
        byte AlliancePartyCount, bool PlayerInCombat, bool BossInCombat, bool BossSfx,
        int RememberedRegulars = 0, bool HistorySaturated = false);
    public EncounterContext EnemyEncounter { get; private set; }
#endif
    private readonly nint moduleStart, moduleEnd;
    private readonly nint callbackVTable;
    private readonly int* frames;
    private readonly int* channels;
    private readonly int* spatialAudio;
    private GameHapticCamera? hapticCamera;
    private GameScreenEffects? screenEffects;
    private uint screenTerritory = uint.MaxValue;
    private nint screenPlayer;
    public string? ScreenEffectsError { get; private set; }
#if BCEF_DIAGNOSTICS
    // Camera timing saves separately from the plugin's SFX segment queue.
    public Task TraceSaveCompletion => screenEffects?.TraceSaveCompletion ?? Task.CompletedTask;
#endif
    public void UpdateScreenEffects(bool allowed, uint territory, nint player)
    {
        var output = Haptics;
        if (output == null) return;
        var current = output.ScreenSettings;
        if (screenTerritory != territory || screenPlayer != player)
        {
            screenTerritory = territory; screenPlayer = player;
            screenEffects?.Reset();
            output.SetScreenEffects(null, current);
        }
        try
        {
            screenEffects?.Update(allowed);
            if (screenEffects != null) ScreenEffectsError = screenEffects.Error;
        }
        catch { screenEffects?.Reset(); output.SetScreenEffects(null, current); ScreenEffectsError = "Screen effect observation stopped."; }
    }
#if BCEF_DIAGNOSTICS
    private long matchedBlocks, rejectedVoices;
#endif
    private int faulted, playerFaulted, disposed;
    public string? ConfirmationError { get; private set; }
    private int sourceScope = (int)SfxSource.PlayerOnly;
    private int localObjectIndex = -1;
#if BCEF_DIAGNOSTICS
    private long ownAnimations, otherAnimations, unknownAnimations, ownedReservations, playbackLinks;
    private int tracing;
    private readonly long[] traceRoots = new long[512], traceTimes = new long[512], tracePeaks = new long[512];
#endif
    // Playback allocates SoundData synchronously inside this actor-aware callback;
    // the actual resource/PCM can finish loading later on another thread.
    [ThreadStatic] private static GameAudioLayout.PlayerIdentity? soundCaster;
    [ThreadStatic] private static byte soundOrigin;
    [ThreadStatic] private static nint sourceActor, sourceDraw;
    [ThreadStatic] private static nint activeSoundClip;
    [ThreadStatic] private static int sourceTimelineOwnerIndex;
    [ThreadStatic] private static EnemyActionTag soundTimelineTag;
    [ThreadStatic] private static uint sourceEffectGetter;
    [ThreadStatic] private static bool soundSelfTargetedExecution;

    public SfxLoudness Loudness { get; } = new();
#if BCEF_DIAGNOSTICS
    public long MatchedBlocks => Interlocked.Read(ref matchedBlocks);
    public long RejectedVoices => Interlocked.Read(ref rejectedVoices);
#endif
    public bool Faulted => Volatile.Read(ref faulted) != 0;
    public bool LayoutSupported => GameAudioLayout.SampleCount(*frames, *channels, *spatialAudio, 0) != 0;
    public bool PlayerFilterSupported => ownership != null && Volatile.Read(ref playerFaulted) == 0;
#if BCEF_DIAGNOSTICS
    public long OwnAnimations => Interlocked.Read(ref ownAnimations);
    public long OtherAnimations => Interlocked.Read(ref otherAnimations);
    public long UnknownAnimations => Interlocked.Read(ref unknownAnimations);
    public long OwnedReservations => Interlocked.Read(ref ownedReservations);
    public long PlaybackLinks => Interlocked.Read(ref playbackLinks);
#endif
    public string? PlayerFilterError { get; private set; }
    public NativeHapticOutput? Haptics { get; }
    public string? HapticsError { get; }
    public string? SpatialError { get; private set; }
    public nint PlayerAddress => ownership?.Player.Actor ?? 0;
#if BCEF_DIAGNOSTICS
    public readonly record struct VoiceObservation(int SoundId, byte Bus, byte Flags, bool Owned, float Rms,
        HapticRoute HapticRoute, bool Biased, EnemySoundKind Enemy);
    public string[] EnemyTimelineKeys => enemySkills?.RecordedKeys ?? Array.Empty<string>();
    public EnemySkillFilter.EffectOwner[] EffectOwners => enemySkills?.EffectOwners ?? Array.Empty<EnemySkillFilter.EffectOwner>();
    public EnemySkillFilter.EffectClip[] EffectClips => enemySkills?.EffectClips ?? Array.Empty<EnemySkillFilter.EffectClip>();
    public EnemySkillFilter.ActionResult[] ActionResults => enemySkills?.ActionResults ?? Array.Empty<EnemySkillFilter.ActionResult>();
    public void SetTracing(bool value)
    {
        if (value) enemySkills?.ClearRecordedKeys();
        enemySkills?.SetTracing(value);
        if (value) nativeTrace?.Start();
        else nativeTrace?.Stop();
        npcSpawns?.SetTracing(value);
        screenEffects?.SetTracing(value);
        Volatile.Write(ref tracing, value ? 1 : 0);
    }
    public GameAudioLayout.SoundReservation[] Reservations() => ownership?.Snapshot() ?? Array.Empty<GameAudioLayout.SoundReservation>();
    public GameAudioLayout.EnemyActor[] EnemyActors() => ownership?.EnemyActors() ?? Array.Empty<GameAudioLayout.EnemyActor>();

    public VoiceObservation[] Voices()
    {
        var result = new List<VoiceObservation>();
        var cutoff = Stopwatch.GetTimestamp() - Stopwatch.Frequency / 10;
        for (var slot = 0; slot < traceRoots.Length; slot++)
        {
            var metadata = Volatile.Read(ref traceRoots[slot]);
            if (metadata == 0 || Volatile.Read(ref traceTimes[slot]) < cutoff) continue;
            var id = (int)metadata;
            var peak = Interlocked.Exchange(ref tracePeaks[slot], (long)((ulong)(uint)id << 32));
            var rms = (int)(peak >> 32) == id ? BitConverter.UInt32BitsToSingle((uint)peak) : 0;
            result.Add(new VoiceObservation(id, (byte)(metadata >> 32), (byte)(metadata >> 40), (metadata & (1L << 48)) != 0,
                rms, (HapticRoute)((metadata >> 49) & 255), (metadata & (1L << 57)) != 0, (EnemySoundKind)((metadata >> 58) & 3)));
        }
        return result.ToArray();
    }

    private void TraceVoice(int id, byte bus, byte flags, bool owned, ReadOnlySpan<float> samples,
        NativeHapticOutput.Frame haptic, EnemySoundKind enemy)
    {
        if (id <= 0) return;
        var slot = id & 511;
        var bits = BitConverter.SingleToUInt32Bits(SfxLoudness.MeasureRms(samples));
        var before = Volatile.Read(ref tracePeaks[slot]);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if ((int)(before >> 32) == id && (uint)before >= bits) break;
            var after = (long)(((ulong)(uint)id << 32) | bits);
            var actual = Interlocked.CompareExchange(ref tracePeaks[slot], after, before);
            if (actual == before) break;
            before = actual;
        }
        Volatile.Write(ref traceRoots[slot], (long)(uint)id | ((long)bus << 32) | ((long)flags << 40) |
            (owned ? 1L << 48 : 0) | ((long)haptic.Route << 49) | (haptic.Biased ? 1L << 57 : 0) | ((long)enemy << 58));
        Volatile.Write(ref traceTimes[slot], Stopwatch.GetTimestamp());
    }

#endif
    public void SetPlayer(nint actor, uint entityId, nint drawObject, Vector3? characterPosition, bool mounted = false)
    {
        Haptics?.SetFootstepContext(actor != 0, mounted);
        enemySkills?.SetImpactDistance(Haptics is { Active: true, CurrentSettings.DistanceFalloff: true });
        if (ownership == null) return;
        try
        {
            Haptics?.SetListener(actor != 0 && characterPosition.HasValue && Haptics.WantsSpatial
                ? hapticCamera?.ReadListener(characterPosition.Value) : null);
        }
        catch { SpatialError = "Spatial listener is unavailable; using original combat haptics."; Haptics?.SetListener(null); }
        Volatile.Write(ref localObjectIndex, actor == 0 ? -1 : *(ushort*)(actor + 0x8C));
        // Only traverse the known local model, on the framework thread. A listener's
        // arbitrary member is never dereferenced as a draw object.
        Span<nint> models = stackalloc nint[32];
        var count = ReadDrawModels(drawObject, models);
        if (ownership.SetPlayer(actor, entityId, drawObject, models[..count]))
        { Loudness.SetActive(false); Haptics?.ResetImpactCue(); }
    }

    private int ReadDrawModels(nint drawObject, Span<nint> models)
    {
        var count = drawObject == 0 ? 0 : 1;
        if (count != 0) models[0] = drawObject;
        for (var parent = 0; parent < count && count < models.Length; parent++)
        {
            var child = *(nint*)(models[parent] + 0x30);
            for (var siblings = 0; child != 0 && siblings < 32 && count < models.Length; siblings++)
            {
                if (models[..count].Contains(child)) break;
                if (*(nint*)(child + 0x18) != models[parent]) break;
                var table = *(nint*)child;
                if (table >= moduleStart && table <= moduleEnd - 24)
                {
                    var getType = *(nint*)(table + 16);
                    // CharacterBase (3) includes weapon models. VFX attached to us
                    // can belong to another actor and must not grant ownership.
                    if (getType >= moduleStart && getType < moduleEnd &&
                        ((delegate* unmanaged<nint, int>)getType)(child) == 3) models[count++] = child;
                }
                child = *(nint*)(child + 0x28);
            }
        }
        return count;
    }

    public void SetEnemies(ReadOnlySpan<GameAudioLayout.EnemyActor> actors, uint territory, bool allowed
#if BCEF_DIAGNOSTICS
        , EncounterContext encounter = default
#endif
        )
    {
        // Only the framework thread reads ObjectTable; callbacks use this immutable roster.
        allowed &= PlayerFilterSupported;
#if BCEF_DIAGNOSTICS
        EnemyEncounter = encounter with { BossSfx = allowed && encounter.BossSfx };
#endif
        var sourceCount = 0;
        if (allowed && !EnemySkillsReady)
        {
            // Exact NPC ownership remains usable if optional action observation
            // fails. Legacy skill/hit routes must not retain stale action tags.
            Span<GameAudioLayout.EnemyActor> sources = stackalloc GameAudioLayout.EnemyActor[actors.Length];
            foreach (var actor in actors)
                if (actor.Kind == EnemySoundKind.Boss && actor.Role == EnemyActorRole.BossEncounter)
                    sources[sourceCount++] = actor;
            ownership?.SetEnemies(sources[..sourceCount], territory);
        }
        else ownership?.SetEnemies(allowed ? actors : default, territory);
        Haptics?.SetEnemyContext(allowed && (EnemySkillsReady || sourceCount != 0));
        if (!allowed || ownership == null) return;
        Span<nint> models = stackalloc nint[32];
        foreach (var actor in actors)
        {
            var count = ReadDrawModels(actor.DrawObject, models);
            ownership.ResolveCaster(actor.Actor, 0)?.SetDrawChildren(models[..count]);
        }
    }

    public bool SetSource(SfxSource source)
    {
        if (Volatile.Read(ref sourceScope) == (int)source) return false;
        // Invalidate observations classified with the old source, including callbacks
        // still inside original playback. The worker re-enables capture afterwards.
        Loudness.SetActive(false);
        Volatile.Write(ref sourceScope, (int)source);
        return true;
    }

    // The validated executable returns zero in EAX; retain the original return value as well as its arguments.
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int RenderDelegate(GameAudioLayout.SourceVoice* voice, float* scratch);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SubmixRenderDelegate(nint submix);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint AnimationSoundDelegate(nint node, nint unused, float timeOffset);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate GameAudioLayout.SoundData* AcquireSoundDelegate(nint manager);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint ResolveSoundDelegate(nint layout, int reservationId);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint CharacterSoundDelegate(nint container, int soundNumber, int unknown2, nint target,
        ulong autoRelease, int weaponIndex, int volumeCategory, ulong unknown7);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ClipUpdateDelegate(nint clip);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint GetTimelineDelegate(nint clip);

    public SfxAudioCapture(IGameInteropProvider interop, ISigScanner scanner, IDataManager data,
        Func<ulong, Vector3?>? targetPosition = null
#if BCEF_DIAGNOSTICS
        , bool developmentInstall = false
#endif
        )
    {
        try
        {
            nint Unique(string signature)
            {
                var matches = scanner.ScanAllText(signature);
                if (matches.Length != 1) throw new NotSupportedException("SFX prototype does not recognize this game mixer version.");
                return matches[0];
            }
            nint Relative(string signature, int displacement, int nextInstruction)
            {
                var code = Unique(signature);
                var address = code + nextInstruction + Marshal.ReadInt32(code + displacement);
                var start = scanner.Module.BaseAddress;
                if (address < start || address > start + scanner.Module.ModuleMemorySize - 8)
                    throw new NotSupportedException("SFX mixer address validation failed.");
                return address;
            }
            moduleStart = scanner.Module.BaseAddress;
            moduleEnd = moduleStart + scanner.Module.ModuleMemorySize;
            var function = Unique(RenderSignature);
            var end = scanner.Module.BaseAddress + scanner.Module.ModuleMemorySize;
            if (function > end - 0x2000) throw new NotSupportedException("SFX mixer function is outside its module.");
            var body = new ReadOnlySpan<byte>((void*)function, 0x2000);
            // Fail closed if the source layout changes despite retaining the function's prologue.
            if (body.IndexOf(new byte[] { 0x83, 0xbf, 0xa8, 0, 0, 0, 3 }) < 0 ||
                body.IndexOf(new byte[] { 0x48, 0x8b, 0x8f, 0xb0, 0, 0, 0 }) < 0 ||
                body.IndexOf(new byte[] { 0xf3, 0x0f, 0x10, 0x8f, 0x30, 5, 0, 0 }) < 0 ||
                body.IndexOf(new byte[] { 0xf3, 0x0f, 0x10, 0xbf, 0x38, 9, 0, 0 }) < 0)
                throw new NotSupportedException("SFX source-voice layout is unsupported.");
            callbackVTable = Relative(CallbackSignature, 7, 11);
            var firstCallback = Marshal.ReadIntPtr(callbackVTable);
            if (firstCallback < scanner.Module.BaseAddress || firstCallback >= end)
                throw new NotSupportedException("SFX callback type validation failed.");
            frames = (int*)Relative("48 63 1D ?? ?? ?? ?? E8 ?? ?? ?? ??", 3, 7);
            channels = (int*)Relative("89 15 ?? ?? ?? ?? 0F 11 05 ?? ?? ?? ??", 2, 6);
            spatialAudio = (int*)Relative("39 05 ?? ?? ?? ?? 48 89 5C 24 40", 2, 6);
            try
            {
                var animationFunction = Unique(AnimationSignature);
                var acquireFunction = Relative(AcquireSignature, 1, 5);
                var resolveFunction = Unique(ResolveSignature);
                var characterFunction = Relative("E8 ?? ?? ?? ?? EB 6A 0F BE 83 ?? ?? ?? ??", 1, 5);
                var clipFunction = Unique("40 53 55 56 57 48 81 EC ?? ?? ?? ?? 48 8B F9");
                var timelineFunction = Unique("40 53 48 83 EC 20 48 8B 41 38 48 8B D9 48 85 C0 75 49");
                if (animationFunction > end - 0x260 || acquireFunction > end - 0x70 || resolveFunction > end - 0x100)
                    throw new NotSupportedException("SFX caster callback is outside its module.");
                var animationBody = new ReadOnlySpan<byte>((void*)animationFunction, 0x260);
                var acquireBody = new ReadOnlySpan<byte>((void*)acquireFunction, 0x70);
                var resolveBody = new ReadOnlySpan<byte>((void*)resolveFunction, 0x100);
                if (animationBody.IndexOf(new byte[] { 0xf3, 0x0f, 0x10, 0x89, 0x54, 2, 0, 0 }) < 0 ||
                    animationBody.IndexOf(new byte[] { 0x48, 0x8b, 0x81, 0xf8, 1, 0, 0 }) < 0 ||
                    animationBody.IndexOf(new byte[] { 0x0f, 0xb7, 0x90, 0xa8, 4, 0, 0 }) < 0 ||
                    animationBody.IndexOf(new byte[] { 0x66, 0xc1, 0xea, 0x0d }) < 0 ||
                    animationBody.IndexOf(new byte[] { 0x48, 0x8b, 0x98, 0x70, 2, 0, 0 }) < 0 ||
                    animationBody.IndexOf(new byte[] { 0xff, 0x50, 8 }) < 0 ||
                    acquireBody.IndexOf(new byte[] { 0x48, 0x8b, 0xbe, 0x28, 2, 0, 0 }) < 0 ||
                    acquireBody.IndexOf(new byte[] { 0xc6, 0x47, 0x24, 1 }) < 0 ||
                    resolveBody.IndexOf(new byte[] { 0x39, 0x77, 0x38 }) < 0)
                    throw new NotSupportedException("SFX caster/slot layout is unsupported.");
                if (characterFunction > end - 0x580 || new ReadOnlySpan<byte>((void*)characterFunction, 0x580)
                    .IndexOf(new byte[] { 0x48, 0x8b, 0x49, 8, 0x48, 0x8b, 1 }) < 0)
                    throw new NotSupportedException("Character sound owner layout is unsupported.");
                // The sound-clip updater calls this lazy timeline resolver before any
                // sound emission. Observe that existing call so first-use clips work too.
                if (clipFunction > end - 0x400 || timelineFunction > end - 0x60 ||
                    *(byte*)(clipFunction + 15) != 0xE8 || clipFunction + 20 + *(int*)(clipFunction + 16) != timelineFunction ||
                    new ReadOnlySpan<byte>((void*)timelineFunction, 0x60).IndexOf(new byte[] { 0x48, 0x89, 0x43, 0x38 }) < 0)
                    throw new NotSupportedException("Sound timeline ownership layout is unsupported.");
                // A layout entry forwards position through its controller at +8;
                // the validated SoundController ID getter reads +8 again (entry+0x10).
                // Multiple positional setter variants share this exact field access.
                // It is a layout check, not a hook target that needs a unique match.
                if (scanner.ScanAllText("48 8D 4F 08 0F 10 03 0F 11 40 20 48 8B 97 68 01 00 00 48 83 C2 20").Length == 0)
                    throw new NotSupportedException("SFX positional controller layout is unsupported.");
                Unique("8B 49 08 83 F9 01 7C 09 E8 ?? ?? ?? ?? 85 C0 75 03 8B 43 08");
                // RootSound vfunc 63 is the SoundController's full, playback-specific ID.
                Unique("8B 41 0C C3 CC CC CC CC CC CC CC CC CC CC CC CC 0F B6 81 88 02 00 00 C3");
                var manager = Marshal.ReadIntPtr(Relative("48 89 35 ?? ?? ?? ?? 48 83 C4 20", 3, 7));
                var pool = manager == 0 ? 0 : Marshal.ReadIntPtr(manager + 0x220);
                if (pool == 0 || ((nuint)pool & 7) != 0) throw new NotSupportedException("SFX sound pool is unavailable.");
                ownership = new GameAudioLayout.PlayerSoundOwnership((GameAudioLayout.SoundData*)pool, EnemySoundCueFilter.Read);
#if BCEF_DIAGNOSTICS
                nativeTrace = new SfxNativeTrace(moduleStart, moduleEnd, (GameAudioLayout.SoundData*)pool);
#endif
                try
                {
                    var playback = new SpecialSoundPlayback(scanner);
                    if (playback.Error != null) arenaSoundError = playback.Error;
                    else arenaSounds = new ArenaSoundEffects(scanner, data, pool, playback);
                }
                catch (Exception error) { arenaSoundError = error.Message; }
                acquire = interop.HookFromAddress<AcquireSoundDelegate>(acquireFunction, AcquireSound);
                resolve = interop.HookFromAddress<ResolveSoundDelegate>(resolveFunction, ResolveSound);
                characterSound = interop.HookFromAddress<CharacterSoundDelegate>(characterFunction, CharacterSound);
                getTimeline = interop.HookFromAddress<GetTimelineDelegate>(timelineFunction, GetTimeline);
                clipUpdate = interop.HookFromAddress<ClipUpdateDelegate>(clipFunction, ClipUpdate);
                animation = interop.HookFromAddress<AnimationSoundDelegate>(animationFunction, AnimationSound);
                acquire.Enable();
                resolve.Enable();
                characterSound.Enable();
                getTimeline.Enable();
                clipUpdate.Enable();
                animation.Enable();
            }
            catch (Exception error)
            {
                try { ResourceCleanup.Run(() => arenaSounds?.Dispose(), () => animation?.Dispose(),
                    () => acquire?.Dispose(), () => resolve?.Dispose(), () => characterSound?.Dispose(),
                    () => clipUpdate?.Dispose(), () => getTimeline?.Dispose()); }
                catch { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error); }
                arenaSounds = null; animation = null; acquire = null; resolve = null;
                characterSound = null; clipUpdate = null; getTimeline = null;
                ownership = null; // The existing All combat option remains usable.
                PlayerFilterError = error.Message;
            }
            if (ownership != null)
            {
                try { enemySkills = new EnemySkillFilter(interop, scanner, data, ownership, targetPosition); }
                catch (Exception error) { enemySkillError = error.Message; }
#if BCEF_DIAGNOSTICS
                try { npcSpawns = new NpcSpawnObserver(interop, scanner); }
                catch (Exception error) { spawnTraceError = error.Message; }
#endif
            }
            try
            {
                var instances = Relative("48 83 EC 28 48 63 D1 48 8D 0D ?? ?? ?? ??", 10, 14);
                var mix = Unique("48 8B C4 53 55 56 48 81 EC 80 00 00 00 80 B9 3C 01 00 00 00 48 8B DA");
                if (instances > end - 15 * 8 || body.IndexOf(new byte[] { 0x48, 0x8d, 0x9f, 0x48, 9, 0, 0 }) < 0 ||
                    body.IndexOf(new byte[] { 0x41, 0xff, 0x51, 0x78 }) < 0)
                    throw new NotSupportedException("Native vibration routing layout is unsupported.");
                Haptics = new NativeHapticOutput((nint*)instances, mix, moduleStart, moduleEnd);
            }
            catch (Exception error)
            {
                HapticsError = error.Message;
                // Ordinary rumble shares classification and distance logic, but does
                // not require a native DualSense vibration destination.
                Haptics = new NativeHapticOutput(null, 0, moduleStart, moduleEnd);
            }
            if (Haptics != null)
            {
                try
                {
                    // Validated against the native mix pass: this function runs once
                    // per submix, after source voices and before effects/sends.
                    var submixRender = Unique("48 89 5C 24 20 56 57 41 55 41 56 41 57 48 83 EC 70 0F B7 81 22 01 00 00");
                    if (submixRender > end - 0x480 ||
                        new ReadOnlySpan<byte>((void*)submixRender, 0x40).IndexOf(new byte[] { 0x66, 0x89, 0x81, 0x24, 0x01, 0, 0 }) < 0 ||
                        new ReadOnlySpan<byte>((void*)submixRender, 0x80).IndexOf(new byte[] { 0x48, 0x8B, 0x8B, 0x80, 0, 0, 0 }) < 0)
                        throw new NotSupportedException("Confirmation vibration mixer layout is unsupported.");
                    sampleRate = (int*)Relative("44 89 0D ?? ?? ?? ?? 44 89 25 ?? ?? ?? ??", 3, 7);
                    // Mix current screen feedback at the existing final submix stage.
                    confirmationRender = interop.HookFromAddress<SubmixRenderDelegate>(submixRender, RenderConfirmation);
                }
                catch (Exception error) { ConfirmationError = error.Message; }
                try { screenEffects = new GameScreenEffects(interop, scanner, Haptics
#if BCEF_DIAGNOSTICS
                    , developmentInstall
#endif
                    ); }
                catch (Exception error) { ScreenEffectsError = error.Message; }
                try
                {
                    var control = Relative("4C 8D 35 ?? ?? ?? ?? 48 8B 09", 3, 7);
                    var getCamera = Relative("E8 ?? ?? ?? ?? 45 32 FF 40 32 FF", 1, 5);
                    var matrixBuilder = Unique("48 8B C4 53 48 81 EC F0 00 00 00 F3 0F 10 05 ?? ?? ?? ?? 48 8B D9");
                    var playSound = Relative("E8 ?? ?? ?? ?? 83 FB ?? 41 BF", 1, 5);
                    if (control > end - 0x190 || getCamera > end - 0x30 || playSound > end - 0x300 ||
                        new ReadOnlySpan<byte>((void*)playSound, 0x300).IndexOf(new byte[] { 0xf3, 0x0f, 0x11, 0x7b, 0x68 }) < 0 ||
                        new ReadOnlySpan<byte>((void*)playSound, 0x300).IndexOf(new byte[] { 0x88, 0x83, 0xb9, 0, 0, 0 }) < 0 ||
                        !new ReadOnlySpan<byte>((void*)getCamera, 8).SequenceEqual(
                        new byte[] { 0xf3, 0x0f, 0x10, 0x89, 0x80, 1, 0, 0 }))
                        throw new NotSupportedException("Camera or positional sound layout is unsupported.");
                    hapticCamera = new GameHapticCamera(control, getCamera);
                    Haptics.SetSpatialMatrices(GameHapticCamera.StereoMatrix(matrixBuilder, 8), GameHapticCamera.StereoMatrix(matrixBuilder, 16));
                }
                catch (Exception error) { SpatialError = error.Message; }
            }
            render = interop.HookFromAddress<RenderDelegate>(function, Render);
            render.Enable();
        }
        catch
        {
            // Includes failures between optional observers and the final render hook.
            // Keep the original initialization error after releasing every acquired hook.
            try { Dispose(); } catch { }
            throw;
        }
    }

    public void UpdateEnableCue(bool request)
    {
        var haptics = Haptics;
        if (confirmationRender == null || haptics == null) return;
        try
        {
            if (request && !haptics.UsesRumble && HapticsError == null && ConfirmationError == null) haptics.RequestEnableCue();
            if (!haptics.UsesRumble && HapticsError == null && ConfirmationError == null && (haptics.HasEnableCue || haptics.ScreenEffectsActive))
            {
                if (!confirmationRender.IsEnabled) confirmationRender.Enable();
            }
            else
            {
                haptics.CancelEnableCue();
                if (confirmationRender.IsEnabled) confirmationRender.Disable();
            }
        }
        catch (Exception error) { haptics.CancelEnableCue(); ConfirmationError = error.Message; }
    }

    private int RenderConfirmation(nint submix)
    {
        if (Haptics is { } output && (output.HasEnableCue || output.ScreenEffectsActive) && Volatile.Read(ref disposed) == 0)
        {
            try
            {
                if (submix != 0 && new ReadOnlySpan<byte>((void*)(submix + 0x13E), 10).SequenceEqual("Vibration\0"u8))
                {
                    var foreground = Native.IsCurrentProcessForeground();
                    var supported = !Faulted && GameAudioLayout.SampleCount(*frames, *channels, *spatialAudio, 0) > 0 &&
                        *sampleRate == HapticEnableCue.SampleRate;
                    output.MixEnableCue(submix, foreground, supported);
                    output.MixScreenEffects(submix, foreground, supported, Stopwatch.GetTimestamp());
#if BCEF_DIAGNOSTICS
                    if (Volatile.Read(ref tracing) != 0) screenEffects?.RecordOutput(output.LastScreenMix);
#endif
                }
            }
            catch { Haptics.CancelEnableCue(); ConfirmationError = "Confirmation vibration stopped."; }
        }
        return confirmationRender!.OriginalDisposeSafe(submix);
    }

    private nint AnimationSound(nint node, nint unused, float timeOffset)
    {
        var previousGetter = sourceEffectGetter; sourceEffectGetter = 0;
        var previous = soundCaster;
        var previousTag = soundTimelineTag; soundTimelineTag = default;
        var previousOrigin = soundOrigin; var previousActor = sourceActor; var previousDraw = sourceDraw;
        soundCaster = null; // Unknown/nested callbacks must not inherit an outer actor.
        soundOrigin = 1; sourceActor = 0; sourceDraw = 0;
        try
        {
            if (PlayerFilterSupported && !Faulted && Volatile.Read(ref disposed) == 0 && node != 0)
            {
                try
                {
                    var time = *(float*)(node + 0x254);
                    var intermediate = *(nint*)(node + 0x1F8);
                    if (time >= 0 && intermediate != 0 && (*(ushort*)(intermediate + 0x4A8) & 0x2000) != 0)
                    {
                        var listener = *(nint*)(intermediate + 0x270);
                        var table = listener == 0 ? 0 : *(nint*)listener;
                        var caster = (nint)0;
                        var drawObject = (nint)0;
                        if (table >= moduleStart && table <= moduleEnd - 16)
                        {
                            var function = *(nint*)(table + 8);
                            if (function >= moduleStart && function < moduleEnd)
                            {
                                sourceEffectGetter = (uint)(function - moduleStart);
                                caster = ((delegate* unmanaged<nint, nint>)function)(listener);
                                if (caster == 0) drawObject = *(nint*)(listener + 8);
                            }
                        }
                        sourceActor = caster; sourceDraw = drawObject;
                        soundCaster = ownership!.ResolveCaster(caster, drawObject);
                        soundTimelineTag = enemySkills?.EffectTag(listener, table, soundCaster) ?? default;
#if BCEF_DIAGNOSTICS
                        if (Volatile.Read(ref tracing) != 0)
                            enemySkills?.RecordEffectOwner(soundCaster, node, intermediate, listener, sourceEffectGetter);
                        if (soundCaster != null && ReferenceEquals(soundCaster, ownership.Player))
                        {
                            Interlocked.Increment(ref ownAnimations);
                        }
                        else if (caster != 0 || drawObject != 0) Interlocked.Increment(ref otherAnimations);
                        else Interlocked.Increment(ref unknownAnimations);
#endif
                    }
                }
                catch { FaultPlayerFilter(); }
            }
            if (soundTimelineTag.Kind == EnemyActionKind.Unknown && soundCaster != null && ReferenceEquals(soundCaster, previous))
                soundTimelineTag = previousTag;
            // Preserve playback, all native parameters and the native return value.
            return animation!.OriginalDisposeSafe(node, unused, timeOffset);
        }
        finally { soundCaster = previous; soundTimelineTag = previousTag; soundOrigin = previousOrigin; sourceActor = previousActor; sourceDraw = previousDraw; sourceEffectGetter = previousGetter; }
    }

    private nint CharacterSound(nint container, int soundNumber, int unknown2, nint target, ulong autoRelease,
        int weaponIndex, int volumeCategory, ulong unknown7)
    {
        var previous = soundCaster;
        var previousTag = soundTimelineTag; soundTimelineTag = default;
        var previousOrigin = soundOrigin; var previousActor = sourceActor; var previousDraw = sourceDraw;
        soundCaster = null; soundOrigin = 2; sourceActor = 0; sourceDraw = 0;
        try
        {
            if (container != 0 && PlayerFilterSupported && Volatile.Read(ref disposed) == 0)
            {
                try
                {
                    sourceActor = *(nint*)(container + 8);
                    soundCaster = ownership!.ResolveCaster(sourceActor, 0);
                }
                catch { FaultPlayerFilter(); }
            }
            if (soundCaster != null && ReferenceEquals(soundCaster, previous)) soundTimelineTag = previousTag;
            return characterSound!.OriginalDisposeSafe(container, soundNumber, unknown2, target, autoRelease, weaponIndex, volumeCategory, unknown7);
        }
        finally { soundCaster = previous; soundTimelineTag = previousTag; soundOrigin = previousOrigin; sourceActor = previousActor; sourceDraw = previousDraw; }
    }

    private GameAudioLayout.SoundData* AcquireSound(nint manager)
    {
        var sound = acquire!.OriginalDisposeSafe(manager);
#if BCEF_DIAGNOSTICS
        if (Volatile.Read(ref tracing) != 0)
        {
            try
            {
                var scope = enemySkills?.DiagnosticScope ?? default;
                nativeTrace?.ObserveAcquisition(sound, soundOrigin, scope.Actor, scope.Entity, scope.Action);
            }
            catch { /* Optional metadata must not fault audio ownership. */ }
        }
#endif
        try
        {
            var action = enemySkills?.Classify(soundCaster, soundTimelineTag, soundOrigin) ?? default;
            var impact = enemySkills?.ImpactForSound(soundCaster, soundTimelineTag, soundOrigin);
            ownership?.Acquired(sound, soundCaster, soundOrigin, sourceActor, sourceDraw,
                activeSoundClip == 0 ? -1 : sourceTimelineOwnerIndex, action, soundOrigin == 1 ? sourceEffectGetter : 0, impact,
                soundOrigin == 3 && soundSelfTargetedExecution);
#if BCEF_DIAGNOSTICS
            if (sound != null && soundCaster != null && ReferenceEquals(soundCaster, ownership?.Player))
                Interlocked.Increment(ref ownedReservations);
#endif
        }
        catch { FaultPlayerFilter(); }
        return sound;
    }

    private void ClipUpdate(nint clip)
    {
        var previousSelfTargeted = soundSelfTargetedExecution; soundSelfTargetedExecution = false;
        var previous = soundCaster;
        var previousTag = soundTimelineTag; soundTimelineTag = default;
        var previousClip = activeSoundClip;
        var previousIndex = sourceTimelineOwnerIndex;
        var previousOrigin = soundOrigin; var previousActor = sourceActor; var previousDraw = sourceDraw;
        soundCaster = null; soundOrigin = 3; sourceActor = 0; sourceDraw = 0; activeSoundClip = clip;
        sourceTimelineOwnerIndex = -1;
        try { clipUpdate!.OriginalDisposeSafe(clip); }
        finally
        {
            soundCaster = previous; soundTimelineTag = previousTag; activeSoundClip = previousClip;
            sourceTimelineOwnerIndex = previousIndex;
            soundSelfTargetedExecution = previousSelfTargeted;
            soundOrigin = previousOrigin; sourceActor = previousActor; sourceDraw = previousDraw;
        }
    }

    private nint GetTimeline(nint clip)
    {
        var timeline = getTimeline!.OriginalDisposeSafe(clip);
        // C012 AVFX can play sound well after its timeline call has returned.
        // Observe its concrete scene object without expanding the sound scope.
        var observeEffect = clip != activeSoundClip && (
#if BCEF_DIAGNOSTICS
            Volatile.Read(ref tracing) != 0 ||
#endif
            enemySkills?.EffectBindingSupported == true && GameAudioLayout.IsEffectClip(clip));
        var previousCaster = soundCaster; var previousTag = soundTimelineTag;
        var previousSelfTargeted = soundSelfTargetedExecution;
        var previousActor = sourceActor; var previousIndex = sourceTimelineOwnerIndex;
        if (clip != 0 && (clip == activeSoundClip || observeEffect) && PlayerFilterSupported && Volatile.Read(ref disposed) == 0)
        {
            try
            {
                soundCaster = null; soundTimelineTag = default; sourceActor = 0;
                soundSelfTargetedExecution = false;
                sourceTimelineOwnerIndex = -1;
                if (timeline != 0)
                {
                    var table = *(nint*)timeline;
                    if (table >= moduleStart && table <= moduleEnd - 30 * 8)
                    {
                        // SchedulerTimeline.GetOwningGameObjectIndex is vfunc 29,
                        // not the clip's target/victim index or a sound-volume group.
                        var function = *(nint*)(table + 29 * 8);
                        if (function >= moduleStart && function <= moduleEnd - 7 &&
                            *(uint*)function == 0x018C818B && *(ushort*)(function + 4) == 0 && *(byte*)(function + 6) == 0xC3)
                        {
                            // Validate the concrete getter's field access before reading
                            // it. Do not invoke an unfamiliar timeline virtual function.
                            var ownerIndex = *(int*)(timeline + 0x18C);
                            sourceTimelineOwnerIndex = ownerIndex;
                            soundCaster = ownership!.ResolveTimelineCaster(ownerIndex, Volatile.Read(ref localObjectIndex));
                            sourceActor = soundCaster?.Actor ?? 0;
                            if (soundCaster != null)
                            {
                                if (enemySkills != null)
                                    soundTimelineTag = enemySkills.Timeline(timeline, table, soundCaster,
#if BCEF_DIAGNOSTICS
                                        Volatile.Read(ref tracing) != 0,
#endif
                                        out soundSelfTargetedExecution, ownerIndex, clip);
                            }
                            if (observeEffect)
                            {
                                enemySkills?.ObserveEffect(clip, soundCaster, soundTimelineTag);
#if BCEF_DIAGNOSTICS
                                if (Volatile.Read(ref tracing) != 0) enemySkills?.RecordEffectClip(clip, soundCaster, soundTimelineTag);
#endif
                            }
                        }
                    }
                }
            }
            catch { FaultPlayerFilter(); }
            finally
            {
                if (observeEffect)
                {
                    soundCaster = previousCaster; soundTimelineTag = previousTag;
                    soundSelfTargetedExecution = previousSelfTargeted;
                    sourceActor = previousActor; sourceTimelineOwnerIndex = previousIndex;
                }
            }
        }
        return timeline;
    }

    private void FaultPlayerFilter()
    {
        Interlocked.Exchange(ref playerFaulted, 1);
        Loudness.SetActive(false);
    }

    private nint ResolveSound(nint layout, int reservationId)
    {
        // Observe the game's own locked lookup. Never traverse its mutable native
        // lists or acquire an audio-engine lock from the mixer callback ourselves.
        var entry = resolve!.OriginalDisposeSafe(layout, reservationId);
#if BCEF_DIAGNOSTICS
        if (Volatile.Read(ref tracing) != 0)
        {
            try { nativeTrace?.ObserveResolution(layout, reservationId, entry); }
            catch { /* Optional metadata must not fault audio ownership. */ }
        }
#endif
        if (entry != 0 && PlayerFilterSupported && Volatile.Read(ref disposed) == 0)
        {
            try
            {
                if (*(int*)(entry + 0x38) == reservationId &&
                    ownership!.LinkPlayback(reservationId, *(int*)(entry + 0x10)))
                {
#if BCEF_DIAGNOSTICS
                    Interlocked.Increment(ref playbackLinks);
#endif
                }
            }
            catch { FaultPlayerFilter(); }
        }
        return entry;
    }

    private int Render(GameAudioLayout.SourceVoice* voice, float* scratch)
    {
        var count = 0;
#if BCEF_DIAGNOSTICS
        var traceCount = 0;
        var traceId = 0;
        byte traceBus = 0, traceFlags = 0;
#endif
        var owned = false;
        var hitReaction = false;
        var enemy = EnemySoundKind.None;
        var output = Haptics;
        var hapticsActive = output?.Active == true;
        var current = hapticsActive ? output!.CurrentSettings : null;
        var enemiesActive = hapticsActive && output!.EnemiesActive;
        var hitReactionsActive = hapticsActive && output!.HitReactionsActive;
        NativeHapticOutput.Frame hapticFrame = default;
        var hapticCount = 0;
        uint hapticFormat = 0;
        var generation = Loudness.Generation;
        var needsVoice = SfxCapturePolicy.NeedsVoice((generation & 1) != 0, current,
            enemiesActive, hitReactionsActive, hapticsActive && output!.FootstepsActive);
#if BCEF_DIAGNOSTICS
        needsVoice |= Volatile.Read(ref tracing) != 0;
#endif
        // Classify while the game's callback/track links still describe this voice,
        // before the original can finish it and release its owner back into a pool.
        if (needsVoice && !Faulted && Volatile.Read(ref disposed) == 0)
        {
            try
            {
                var scope = (SfxSource)Volatile.Read(ref sourceScope);
                var sound = scratch == null ? null : GameAudioLayout.SoundOfVoice(voice, callbackVTable);
                if (sound != null)
                {
                    var samples = GameAudioLayout.SampleCount(*frames, *channels, *spatialAudio, voice->OutputFormat);
                    var combat = GameAudioLayout.IsCombatSound(sound->BusId, sound->ExtendedFlags);
                    var inspectEnemies = combat && enemiesActive;
                    var inspectOwnership = SfxCapturePolicy.NeedsOwnership(combat, sound->ExtendedFlags,
                        (generation & 1) != 0 && scope == SfxSource.PlayerOnly, current, enemiesActive, hitReactionsActive, enemiesActive && sound->BusId == 5);
#if BCEF_DIAGNOSTICS
                    var traceActive = Volatile.Read(ref tracing) != 0;
                    inspectEnemies |= combat && traceActive;
                    inspectOwnership |= traceActive;
#endif
                    var inspectPosition = hapticsActive && output!.WantsSpatial;
                    var attribution = PlayerFilterSupported && inspectOwnership
                        ? ownership!.Inspect(sound->SoundId, inspectEnemies, inspectPosition) : default;
                    owned = attribution.Owned;
#if BCEF_DIAGNOSTICS
                    if (traceActive && !owned && sound->BusId == 5)
                    {
                        try { nativeTrace?.ObserveVoice(voice, sound); }
                        catch { /* Optional metadata must not fault native playback. */ }
                    }
#endif
                    hitReaction = attribution.HitReaction;
                    enemy = attribution.Enemy;
                    var arena = !owned && sound->BusId == 5 && (enemiesActive
#if BCEF_DIAGNOSTICS
                        || traceActive
#endif
                        )
                        ? arenaSounds?.Find(sound->SoundId) : null;
                    if (arena.HasValue) enemy = EnemySoundKind.Boss;
                    if ((generation & 1) != 0 && combat &&
                        (scope == SfxSource.AllCombat || PlayerFilterSupported && owned)) count = samples;
#if BCEF_DIAGNOSTICS
                    if (traceActive)
                    { traceCount = samples; traceId = sound->SoundId; traceBus = sound->BusId; traceFlags = (byte)sound->ExtendedFlags; }
#endif
                    if (hapticsActive && samples > 0 && (PlayerFilterSupported || Haptics!.FootstepsActive))
                    {
                        Vector3? position = null;
                        Vector3? distancePosition = null;
                        if (inspectPosition)
                        {
                            if (arena.HasValue) position = arena.Value.Position;
                            else
                            {
                                position = attribution.Position;
                                distancePosition = attribution.Impact?.DistancePosition(position);
                            }
                        }
                        hapticFrame = Haptics!.Prepare(voice, owned, sound->BusId, sound->ExtendedFlags, position, sound->SoundId,
                            enemy, hitReaction, arena.HasValue, distancePosition, attribution.SelfTargetedExecution && !arena.HasValue);
                        if (hapticFrame.HasOutput) { hapticCount = samples; hapticFormat = voice->OutputFormat; }
                    }
                }
#if BCEF_DIAGNOSTICS
                if (count == 0) Interlocked.Increment(ref rejectedVoices);
#endif
            }
            catch { Fault(); }
        }

        // Always call playback exactly once, with the original pointers. No analysis exception
        // may skip this call, and none of the audio samples or native parameters are written.
        var result = render.OriginalDisposeSafe(voice, scratch);
        float? hapticRms = null;
        if (result == 0 && hapticCount > 0) hapticRms = Haptics!.Mix(hapticFrame, scratch, hapticCount, hapticFormat);
#if BCEF_DIAGNOSTICS
        if (traceCount > 0 && Volatile.Read(ref tracing) != 0)
        {
            try { TraceVoice(traceId, traceBus, traceFlags, owned, new ReadOnlySpan<float>(scratch, traceCount), hapticFrame, enemy); }
            catch { Volatile.Write(ref tracing, 0); }
        }
#endif
        if (count > 0 && Loudness.Active && !Faulted)
        {
            try
            {
                if (hapticRms is { } rms && count == hapticCount) Loudness.ObserveLevel(rms, generation);
                else Loudness.Observe(new ReadOnlySpan<float>(scratch, count), generation);
#if BCEF_DIAGNOSTICS
                Interlocked.Increment(ref matchedBlocks);
#endif
            }
            catch { Fault(); }
        }
        return result;
    }

    private void Fault()
    {
        Interlocked.Exchange(ref faulted, 1);
        Loudness.SetActive(false); // Reporting is deferred to the UI; never log from the audio callback.
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
#if BCEF_DIAGNOSTICS
        Volatile.Write(ref tracing, 0);
#endif
        ResourceCleanup.Run(() => Loudness.SetActive(false),
            () => Haptics?.Configure(new HapticSettings(), false, false),
            () => screenEffects?.Dispose(), () => enemySkills?.Dispose(), () => arenaSounds?.Dispose(),
#if BCEF_DIAGNOSTICS
            () => npcSpawns?.Dispose(),
#endif
            () => confirmationRender?.Dispose(), () => animation?.Dispose(),
            () => acquire?.Dispose(), () => resolve?.Dispose(), () => characterSound?.Dispose(),
            () => clipUpdate?.Dispose(), () => getTimeline?.Dispose(), () => render?.Dispose());
    }
}
