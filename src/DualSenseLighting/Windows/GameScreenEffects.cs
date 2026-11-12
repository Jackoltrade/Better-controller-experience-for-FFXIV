// SPDX-License-Identifier: GPL-3.0-only
// Singleton signatures adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
#if BCEF_DIAGNOSTICS
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
#endif
using System.Threading;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>Observes effects immediately after the game composes its camera.</summary>
internal sealed class GameScreenEffects : IDisposable
{
    internal const string PostSignature = "48 8B 0D ?? ?? ?? ?? 8B 81 70 44 00 00 C1 E8 08 A8 01 0F 85";
    internal const string CameraSignature = "48 8B 05 ?? ?? ?? ?? 48 83 C1 10 48 C7 40 ?? ?? ?? ?? ??";
    // Independently checked on the installed 2026.10.06 client: AVFX blur
    // application and camera-quake application, including their field accesses.
    internal const string BlurSignature = "40 53 48 83 EC 40 48 8B 05 ?? ?? ?? ?? 48 8B D9 80 78 28 00 0F 84 ?? ?? ?? ?? 80 B9 4C 01 00 00 00";
    internal const string ShakeSignature = "40 53 48 83 EC 20 48 8B D9 48 8B 0D ?? ?? ?? ?? 48 85 C9 74 6E 48 83 C1 10 BA D3 00 00 00";
    internal const string CameraOffsetsSignature = "F3 44 0F 58 56 20 F3 44 0F 58 6E 40";
    internal const string MixerInputSignature = "39 B5 88 00 00 00 7D 06 89 B5 88 00 00 00 66 FF 85 22 01 00 00";
    internal const string CameraUpdateSignature = "4C 8B DC 49 89 5B 10 49 89 73 18 49 89 7B 20 55 49 8D AB 28 FF FF FF 48 81 EC D0 01 00 00";
    internal const string ScriptedShakeSignature = "40 53 48 83 EC 10 83 39 00 4D 8B D0 4C 8B CA 48 8B D9 75 ?? 33 C0 48 89 02 89 42 08 49 89 00 41 89 40 08";
    internal const string ScriptedSourceSignature = "48 8D 0D ?? ?? ?? ?? 41 0F 28 D4 41 0F 28 DC F3 0F 59 D6 F3 0F 59 DF F3 45 0F 59 E0";
    internal const string ControlSignature = "4C 8D 35 ?? ?? ?? ?? 48 8B 09";
    private readonly nint postGlobal, cameraGlobal, moduleStart, moduleEnd;
    private readonly nint scriptedSource, control;
    private readonly NativeHapticOutput output;
#if BCEF_DIAGNOSTICS
    private ScreenEffectSourceObserver? sourceObserver;
    private string? sourceTraceError;
#endif
    private SecondaryMotionObserver? motionObserver;
    private SecondaryMotionSession? motionSession;
    private KnownMotionFilter? motionFilter;
    private string? motionBindingError;
#if BCEF_DIAGNOSTICS
    private readonly long motionPreparationTicks;
    private readonly long? sourcePreparationTicks;
    private long? firstMotionActivationTicks, firstTraceStartTicks;
#endif
    private readonly Hook<CameraUpdateDelegate> cameraUpdate;
    private Hook<ScriptedShakeDelegate>? scriptedShake;
    private sealed record ScriptedSample(nint SceneCamera, long Timestamp, Vector3 Position, Vector3 Rotation);
    private sealed class CaptureContext
    {
        public readonly CameraShakeTracker Shake = new(), ScriptedShake = new();
        public readonly ScreenEffectFilterGuard FilterGuard = new();
        public nint Camera, SceneCamera;
        public long LastCompositionClock;
        public ScriptedSample? Scripted;
    }
    private CaptureContext context = new();
    private int allowed, disposed, scriptedFaulted;
    private string? error, scriptedError;
#if BCEF_DIAGNOSTICS
    private string? traceError;
#endif
    public string? Error => Volatile.Read(ref error) ?? Volatile.Read(ref scriptedError) ??
#if BCEF_DIAGNOSTICS
        Volatile.Read(ref traceError) ??
#endif
        (output.ScreenEffectsActive ? motionBindingError ?? motionObserver?.Error : null);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void CameraUpdateDelegate(nint manager);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate byte ScriptedShakeDelegate(nint source, nint first, nint second);

#if BCEF_DIAGNOSTICS
    // Opt-in companion to /bcef trace. Native/audio callbacks never wait for disk.
    private const int TraceCapacity = 4096;
    private sealed class TimingSession
    {
        public readonly object Sync = new();
        public readonly DateTimeOffset StartedUtc = DateTimeOffset.UtcNow;
        public readonly long StartedClock = Stopwatch.GetTimestamp();
        public readonly TimingFrame[] Frames = new TimingFrame[TraceCapacity];
        public int Count, Next;
        public long Sequence, Dropped;
        public ScreenEffectSourceRecording? Sources;
    }
    private record struct TimingFrame(long Sequence, long CaptureClock, long IntervalTicks,
        float PositionX, float PositionY, float PositionZ, float RotationX, float RotationY, float RotationZ,
        float RawMagnitude, int UnchangedFrames, float CameraLevel, float TargetGain, string Backend,
        long FirstMixClock, float MixedGain, string OutputState)
    {
        public ScreenEffectKind Effects { get; init; }
        public ScreenEffectKind DominantEffect { get; init; }
        public float ShakeLevel { get; init; }
        public float ManagerShakeLevel { get; init; }
        public float ScriptedShakeLevel { get; init; }
        public float ScriptedRawMagnitude { get; init; }
        public long ScriptedSampleClock { get; init; }
        public long ScriptedObservedClock { get; init; }
        public string? ScriptedSampleState { get; init; }
        public float ScriptedPositionX { get; init; }
        public float ScriptedPositionY { get; init; }
        public float ScriptedRotationX { get; init; }
        public float ScriptedRotationY { get; init; }
        public float PrimaryPositionX { get; init; }
        public float PrimaryPositionY { get; init; }
        public float PrimaryPositionZ { get; init; }
        public float PrimaryRotationX { get; init; }
        public float PrimaryRotationY { get; init; }
        public float PrimaryRotationZ { get; init; }
        public float SecondaryPositionX { get; init; }
        public float SecondaryPositionY { get; init; }
        public float SecondaryPositionZ { get; init; }
        public float SecondaryRotationX { get; init; }
        public float SecondaryRotationY { get; init; }
        public float SecondaryRotationZ { get; init; }
        public float PatternAmplitudeMultiplier { get; init; }
        public NativeHapticOutput.ScreenMixSnapshot? Output { get; set; }
        public double? FirstMixMilliseconds => FirstMixClock > 0
            ? (FirstMixClock - CaptureClock) * 1000.0 / Stopwatch.Frequency : null;
    }
    private TimingSession? timing;
    private Task traceSave = Task.CompletedTask;
    // Snapshot after stopping tracing; callers must prevent a new session while awaiting it.
    public Task TraceSaveCompletion => Volatile.Read(ref traceSave);

#endif

    [DllImport("kernel32.dll")]
    private static extern unsafe bool ReadProcessMemory(nint process, nint address, void* bytes, nuint size, out nuint read);

    public unsafe GameScreenEffects(IGameInteropProvider interop, ISigScanner scanner, NativeHapticOutput output
#if BCEF_DIAGNOSTICS
        , bool developmentInstall = false
#endif
        )
    {
        this.output = output;
        moduleStart = scanner.Module.BaseAddress;
        moduleEnd = checked(moduleStart + scanner.Module.ModuleMemorySize);
        nint Unique(string signature)
        {
            var matches = scanner.ScanAllText(signature);
            if (matches.Length != 1) throw new NotSupportedException("Screen effect layout is unsupported on this game build.");
            return matches[0];
        }
        ReadOnlySpan<byte> Code(nint address, int size)
        {
            if (address < moduleStart || address > moduleEnd - size) throw new NotSupportedException("Screen effect code is outside the game module.");
            // Inspect the original image, even if another plugin hooks this code.
            return new ReadOnlySpan<byte>((void*)(scanner.SearchBase + (address - moduleStart)), size);
        }
        nint Global(string signature)
        {
            var code = Unique(signature);
            var result = checked(code + 7 + BinaryPrimitives.ReadInt32LittleEndian(Code(code, 7)[3..]));
            if (result < moduleStart || result > moduleEnd - 8) throw new NotSupportedException("Screen effect singleton is outside the game module.");
            return result;
        }
        postGlobal = Global(PostSignature);
        cameraGlobal = Global(CameraSignature);
        var composition = Unique(CameraOffsetsSignature);
        var update = Unique(CameraUpdateSignature);
        var cameraCode = Code(update, 0x440);
        if (composition < update || composition >= update + cameraCode.Length ||
            cameraCode.IndexOf(new byte[] { 0x48, 0x63, 0x41, 0x50 }) < 0 ||
            cameraCode.IndexOf(new byte[] { 0x48, 0x8B, 0x4C, 0xC1, 0x58 }) < 0)
            throw new NotSupportedException("Camera update composition is unsupported on this game build.");
        Unique(MixerInputSignature);
        var blur = Code(Unique(BlurSignature), 0x382);
        var shake = Code(Unique(ShakeSignature), 0x89);
        if (blur.IndexOf(new byte[] { 0x89, 0x88, 0xA4, 0x46, 0, 0 }) < 0 ||
            blur.IndexOf(new byte[] { 0x89, 0x88, 0xAC, 0x46, 0, 0 }) < 0 ||
            blur.IndexOf(new byte[] { 0x89, 0x88, 0xC8, 0x47, 0, 0 }) < 0 ||
            blur.IndexOf(new byte[] { 0xC7, 0x80, 0x9C, 0x46, 0, 0, 2, 0, 0, 0 }) < 0 ||
            shake.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x41, 0x14 }) < 0 ||
            shake.IndexOf(new byte[] { 0xF3, 0x0F, 0x11, 0x51, 0x34 }) < 0)
            throw new NotSupportedException("Screen effect parameter layout is unsupported on this game build.");
        // Native wrapper at installed RVA 0xD4470 passes only manager in RCX and
        // tail-jumps to this void update. Preserve the call and arguments exactly.
        cameraUpdate = interop.HookFromAddress<CameraUpdateDelegate>(update, CaptureCameraUpdate);
        // This independent producer also drives Lua/cutscene shakes directly
        // into LowCutCamera's pose, bypassing the scene-manager AVFX offsets.
        // An unsupported optional binding leaves the established paths usable.
        try
        {
            Hook<ScriptedShakeDelegate>? created = null;
            try
            {
                var sampler = Unique(ScriptedShakeSignature);
                var caller = Unique(ScriptedSourceSignature);
                var callerCode = Code(caller, 0x120);
                if (callerCode[0xB8] != 0xE8 ||
                    caller + 0xBD + BinaryPrimitives.ReadInt32LittleEndian(callerCode[0xB9..]) != sampler ||
                    callerCode.IndexOf(new byte[] { 0xF3, 0x0F, 0x58, 0x45, 0xD0 }) < 0 ||
                    callerCode.IndexOf(new byte[] { 0xF3, 0x0F, 0x58, 0x4D, 0xD4 }) < 0)
                    throw new NotSupportedException("Scripted shake camera consumer is unsupported on this game build.");
                scriptedSource = Global(ScriptedSourceSignature);
                control = Global(ControlSignature);
                if (scriptedSource != control + 0x28)
                    throw new NotSupportedException("Scripted shake camera owner is unsupported on this game build.");
                created = interop.HookFromAddress<ScriptedShakeDelegate>(sampler, CaptureScriptedShake);
                scriptedShake = created;
            }
            catch (Exception exception)
            {
                created?.Dispose();
                scriptedError = exception.Message;
            }
            // This constructor already runs in Plugin's audio initialization task.
            // Prepare disabled hooks here; activation must not scan the game image.
#if BCEF_DIAGNOSTICS
            var started = Stopwatch.GetTimestamp();
#endif
            try { motionObserver = new SecondaryMotionObserver(interop, scanner, CurrentMotion); }
            catch (Exception failure) { motionBindingError = failure.Message; }
#if BCEF_DIAGNOSTICS
            finally { motionPreparationTicks = Stopwatch.GetTimestamp() - started; }
            if (developmentInstall)
            {
                started = Stopwatch.GetTimestamp();
                try { sourceObserver = new ScreenEffectSourceObserver(interop, scanner); }
                catch (Exception failure) { sourceTraceError = failure.Message; }
                finally { sourcePreparationTicks = Stopwatch.GetTimestamp() - started; }
            }
#endif
        }
        catch (Exception failure)
        {
            try { ResourceCleanup.Run(
#if BCEF_DIAGNOSTICS
                () => sourceObserver?.Dispose(),
#endif
                () => motionObserver?.Dispose(),
                () => scriptedShake?.Dispose(), cameraUpdate.Dispose); }
            catch (Exception cleanup) { throw new AggregateException("Screen effect initialization and rollback failed.", failure, cleanup); }
            throw;
        }
    }

    // Framework thread publishes permission/context, never stale pre-tick offsets.
    public void Update(bool permitted)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        Volatile.Write(ref allowed, permitted ? 1 : 0);
        var foreground = Native.IsCurrentProcessForeground();
        UpdateMotion(permitted, foreground);
#if BCEF_DIAGNOSTICS
        sourceObserver?.SetActive(permitted && Volatile.Read(ref timing) != null && foreground);
#endif
        var active = permitted && foreground && (output.ScreenEffectsActive
#if BCEF_DIAGNOSTICS
            || Volatile.Read(ref timing) != null
#endif
            );
        if (active)
        {
            try { if (!cameraUpdate.IsEnabled) cameraUpdate.Enable(); }
            catch
            {
                Volatile.Write(ref allowed, 0);
                try { if (cameraUpdate.IsEnabled) cameraUpdate.Disable(); }
                finally
                {
                    try { if (scriptedShake?.IsEnabled == true) scriptedShake.Disable(); }
                    finally { Reset(); }
                }
                throw;
            }
            if (Volatile.Read(ref scriptedFaulted) == 0 && scriptedShake is { IsEnabled: false } sampler)
            {
                try { sampler.Enable(); }
                catch (Exception exception)
                {
                    Volatile.Write(ref scriptedFaulted, 1);
                    try { sampler.Dispose(); scriptedShake = null; }
                    catch { /* Retain the hook so unload can retry disposal. */ }
                    Volatile.Write(ref scriptedError, exception.Message);
                }
            }
        }
        else
        {
            var enabled = cameraUpdate.IsEnabled || scriptedShake?.IsEnabled == true;
            try { if (cameraUpdate.IsEnabled) cameraUpdate.Disable(); }
            finally
            {
                try { if (scriptedShake?.IsEnabled == true) scriptedShake.Disable(); }
                finally { if (enabled) Reset(); }
            }
        }
    }

    private void UpdateMotion(bool permitted, bool foreground)
    {
#if BCEF_DIAGNOSTICS
        var recording = Volatile.Read(ref timing)?.Sources;
#endif
        var filtering = output.ScreenEffectsActive;
        var active = permitted && foreground && (filtering
#if BCEF_DIAGNOSTICS
            || recording != null
#endif
            );
#if BCEF_DIAGNOSTICS
        var started = active && motionObserver != null && !firstMotionActivationTicks.HasValue ? Stopwatch.GetTimestamp() : 0;
        try
        {
#endif
            if (active && motionObserver != null)
            {
                var filter = filtering ? motionFilter ??= new KnownMotionFilter() : null;
                var current = Volatile.Read(ref motionSession);
                if (current == null || !current.Active ||
#if BCEF_DIAGNOSTICS
                    !ReferenceEquals(current.Recording, recording) ||
#endif
                    !ReferenceEquals(current.Filter, filter))
                {
                    if (!ReferenceEquals(current?.Filter, filter)) motionFilter?.Invalidate();
                    var next = new SecondaryMotionSession(
#if BCEF_DIAGNOSTICS
                        recording,
#endif
                        filter);
                    Interlocked.Exchange(ref motionSession, next)?.Retire(Stopwatch.GetTimestamp());
                }
            }
            else
            {
                Interlocked.Exchange(ref motionSession, null)?.Retire(Stopwatch.GetTimestamp());
                motionFilter?.Invalidate();
            }
            motionObserver?.SetActive(active);
            if (motionObserver?.Error != null)
            {
                Interlocked.Exchange(ref motionSession, null)?.Retire(Stopwatch.GetTimestamp());
                motionFilter?.Invalidate();
            }
#if BCEF_DIAGNOSTICS
        }
        finally { if (started != 0) firstMotionActivationTicks = Stopwatch.GetTimestamp() - started; }
#endif
    }

    private SecondaryMotionSession? CurrentMotion()
    {
        var current = Volatile.Read(ref motionSession);
        if (current?.Active == true && Volatile.Read(ref disposed) == 0 && Volatile.Read(ref allowed) != 0 &&
            Native.IsCurrentProcessForeground()) return current;
        current?.Gap(Stopwatch.GetTimestamp());
        return null;
    }

    private byte CaptureScriptedShake(nint source, nint first, nint second)
    {
        var epoch = Volatile.Read(ref context);
        var result = scriptedShake!.OriginalDisposeSafe(source, first, second);
        try
        {
            if (source != scriptedSource || Volatile.Read(ref disposed) != 0 || Volatile.Read(ref scriptedFaulted) != 0 || Volatile.Read(ref allowed) == 0 ||
                !Native.IsCurrentProcessForeground() || !ReferenceEquals(epoch, Volatile.Read(ref context))) return result;
            var lowCut = Pointer(control + 8);
            if (!ValidObject(lowCut) || lowCut > nint.MaxValue - 0x10 || !ValidObject(lowCut + 0x10))
            { Volatile.Write(ref epoch.Scripted, null); return result; }
            Span<byte> firstBytes = stackalloc byte[8];
            Span<byte> secondBytes = stackalloc byte[8];
            if (result != 0 && (!Copy(first, firstBytes) || !Copy(second, secondBytes)))
            { Volatile.Write(ref epoch.Scripted, null); return result; }
            var position = Vector3.Zero;
            var rotation = Vector3.Zero;
            if (result != 0)
            {
                var p = MemoryMarshal.Cast<byte, float>(firstBytes);
                var r = MemoryMarshal.Cast<byte, float>(secondBytes);
                // This consumer uses only X/Y of each authored output. Its
                // unused Z channel must not create vibration absent on screen.
                position = new(p[0], p[1], 0); rotation = new(r[0], r[1], 0);
            }
            var captured = new ScriptedSample(lowCut + 0x10, Stopwatch.GetTimestamp(), position, rotation);
            if (ReferenceEquals(epoch, Volatile.Read(ref context)) && Volatile.Read(ref allowed) != 0)
                Volatile.Write(ref epoch.Scripted, captured);
        }
        catch { Volatile.Write(ref epoch.Scripted, null); Volatile.Write(ref scriptedError, "Scripted shake observation stopped."); }
        return result;
    }

    private void CaptureCameraUpdate(nint manager)
    {
        var epoch = Volatile.Read(ref context);
        // Always run the complete original camera update first, exactly once.
        cameraUpdate.OriginalDisposeSafe(manager);
        if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref allowed) == 0 ||
            !Native.IsCurrentProcessForeground() || !ReferenceEquals(epoch, Volatile.Read(ref context))) return;
        var current = output.ScreenSettings;
        try
        {
            var sample = Read(manager, epoch);
            if (!ReferenceEquals(epoch, Volatile.Read(ref context)) || Volatile.Read(ref allowed) == 0) return;
            output.SetScreenEffects(sample, current);
        }
        catch
        {
            Reset();
            Volatile.Write(ref error, "Screen effect camera observation stopped.");
        }
    }

    private ScreenEffectSample? Read(nint manager, CaptureContext epoch)
    {
        var post = Pointer(postGlobal);
        var camera = Pointer(cameraGlobal);
        Span<byte> parameters = stackalloc byte[0x364];
        Span<byte> offsets = stackalloc byte[0x40];
        Span<byte> indexBytes = stackalloc byte[4];
        if (manager != camera || !ValidObject(post) || !ValidObject(camera) || !Copy(post + 0x4470, parameters) || !Copy(camera + 0x10, offsets) ||
            !Copy(camera + 0x50, indexBytes) ||
            post != Pointer(postGlobal) || camera != Pointer(cameraGlobal))
        { epoch.Shake.Reset(); epoch.ScriptedShake.Reset(); Volatile.Write(ref error, "Waiting for screen effect state."); return null; }
        var index = BinaryPrimitives.ReadInt32LittleEndian(indexBytes);
        var sceneCamera = index is >= 0 and < 14 ? Pointer(camera + 0x58 + index * 8) : 0;
        if (!ValidObject(sceneCamera))
        { epoch.Shake.Reset(); epoch.ScriptedShake.Reset(); Volatile.Write(ref error, "Waiting for active screen camera."); return null; }
        Volatile.Write(ref error, null);
        if (epoch.Camera != camera || epoch.SceneCamera != sceneCamera)
        {
            epoch.Shake.Reset(); epoch.ScriptedShake.Reset();
            // Reset already invalidated the old epoch. Its first camera frame
            // must retain the source writes observed during this same update.
            if (epoch.Camera != 0) motionFilter?.Invalidate();
            epoch.Camera = camera; epoch.SceneCamera = sceneCamera;
        }
        var floats = MemoryMarshal.Cast<byte, float>(parameters);
        var vectors = MemoryMarshal.Cast<byte, float>(offsets);
        var now = Stopwatch.GetTimestamp();
        // The renderer sums both scene-manager offset pairs. The independent
        // LowCut producer has already modified the active camera's base pose.
        var position = new Vector3(vectors[0] + vectors[4], vectors[1] + vectors[5], vectors[2] + vectors[6]);
        var rotation = new Vector3(vectors[8] + vectors[12], vectors[9] + vectors[13], vectors[10] + vectors[14]);
        var managerLevel = epoch.Shake.Observe(position, rotation, now);
        var observedScripted = Volatile.Read(ref epoch.Scripted);
#if BCEF_DIAGNOSTICS
        var scriptedState = observedScripted == null ? "not-observed" : observedScripted.SceneCamera != sceneCamera ? "different-camera" :
            observedScripted.Timestamp > now ? "future" : now - observedScripted.Timestamp > ScreenEffectHaptics.FreshTicks ? "stale" :
            observedScripted.Timestamp <= epoch.LastCompositionClock ? "already-consumed" : "applied";
        var scripted = scriptedState == "applied" ? observedScripted : null;
#else
        var scripted = observedScripted != null && observedScripted.SceneCamera == sceneCamera &&
            observedScripted.Timestamp <= now && now - observedScripted.Timestamp <= ScreenEffectHaptics.FreshTicks &&
            observedScripted.Timestamp > epoch.LastCompositionClock ? observedScripted : null;
#endif
        epoch.LastCompositionClock = now;
        var scriptedLevel = epoch.ScriptedShake.Observe(scripted?.Position ?? Vector3.Zero, scripted?.Rotation ?? Vector3.Zero, now);
        // Select a current observed signal, never add duplicate camera motion.
        var shakeLevel = Math.Max(managerLevel, scriptedLevel);
        var measured = ScreenEffectHaptics.Analyze(shakeLevel,
            BinaryPrimitives.ReadUInt32LittleEndian(parameters), BinaryPrimitives.ReadInt32LittleEndian(parameters[0x22C..]),
            floats[0x234 / 4], floats[0x23C / 4], floats[0x358 / 4],
            new(floats[0x35C / 4], floats[0x360 / 4], floats[0x32C / 4]));
        var multiplier = 1f;
        var motion = Volatile.Read(ref motionSession);
        if (motion?.Active == true && motion.Filter != null && motionObserver?.Error == null)
        {
            multiplier = motion.Filter.GetGain(new(vectors[4], vectors[5], vectors[6]),
                new(vectors[12], vectors[13], vectors[14]), now);
            if (!motion.Active || !ReferenceEquals(motion, Volatile.Read(ref motionSession))) multiplier = 1;
            multiplier = epoch.FilterGuard.Apply(multiplier,
                new(vectors[0], vectors[1], vectors[2]), new(vectors[8], vectors[9], vectors[10]),
                scriptedLevel, measured.Effects, now);
        }
        // If camera frames stop arriving, do not sustain the last jolt for the
        // generic 100 ms screen-effect freshness window on a fast-running game.
        var expiresAt = 0L;
        if (measured.DominantEffect == ScreenEffectKind.Shake)
        {
            var tracker = scriptedLevel > managerLevel ? epoch.ScriptedShake : epoch.Shake;
            var hold = Math.Clamp(Math.Max(tracker.IntervalTicks * 2, Stopwatch.Frequency / 30), 1,
                ScreenEffectHaptics.FreshTicks);
            expiresAt = Math.Min(now + hold, tracker.LastChangeTimestamp + hold);
        }
        // Motion/filter state above must advance even on a quiet frame. Diagnostic
        // recordings retain those frames; production can publish quiet without an object.
#if !BCEF_DIAGNOSTICS
        if (measured.Effects == ScreenEffectKind.None || !float.IsFinite(measured.Level) || measured.Level <= 0)
            return null;
#endif
        var sample = measured.At(now, multiplier, expiresAt);
#if BCEF_DIAGNOSTICS
        RecordCamera(sample, position, rotation, vectors, epoch, managerLevel, scriptedLevel, scripted, observedScripted?.Timestamp ?? 0, scriptedState);
#endif
        return sample;
    }

    public void Reset()
    {
        motionFilter?.Invalidate();
        Volatile.Write(ref context, new CaptureContext());
        output.SetScreenEffects(null, output.ScreenSettings);
    }

#if BCEF_DIAGNOSTICS
    public void SetTracing(bool enabled)
    {
        if (enabled)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            if (Volatile.Read(ref timing) == null)
            {
                var started = firstTraceStartTicks.HasValue ? 0 : Stopwatch.GetTimestamp();
                var next = new TimingSession();
                next.Sources = sourceObserver?.Start(next.StartedClock) ?? new ScreenEffectSourceRecording(next.StartedClock);
                Volatile.Write(ref timing, next);
                if (started != 0) firstTraceStartTicks = Stopwatch.GetTimestamp() - started;
            }
            return;
        }
        var completed = Interlocked.Exchange(ref timing, null);
        if (completed == null) return;
        sourceObserver?.Stop();
        completed.Sources?.Retire();
        var sourceError = sourceTraceError ?? sourceObserver?.Error;
        var motionSupported = motionObserver != null && motionObserver.Error == null;
        var motionError = motionBindingError ?? motionObserver?.Error;
        var activationTiming = new { ClockFrequency = Stopwatch.Frequency, MotionPreparationTicks = motionPreparationTicks,
            SourcePreparationTicks = sourcePreparationTicks, FirstMotionActivationTicks = firstMotionActivationTicks,
            FirstTraceStartTicks = firstTraceStartTicks };
        var previous = traceSave;
        traceSave = Task.Run(async () =>
        {
            try
            {
                await previous.ConfigureAwait(false);
                TimingFrame[] frames;
                lock (completed.Sync)
                {
                    frames = new TimingFrame[completed.Count];
                    for (var i = 0; i < frames.Length; i++)
                        frames[i] = completed.Frames[(completed.Next - completed.Count + i + TraceCapacity) % TraceCapacity];
                }
                var path = TraceArchive.ScreenTimingPath;
                var json = JsonSerializer.Serialize(new { Format = "camera-haptics-timing-v5", Policy = "Independent",
                    PluginVersion = typeof(GameScreenEffects).Assembly.GetName().Version?.ToString(),
                    ScriptedShakeSupported = scriptedShake != null && Volatile.Read(ref scriptedFaulted) == 0,
                    ScriptedShakeError = Volatile.Read(ref scriptedError), ScreenObserverError = Volatile.Read(ref error),
                    completed.StartedUtc, completed.StartedClock, ClockFrequency = Stopwatch.Frequency,
                    Capacity = TraceCapacity, completed.Dropped, Overwritten = Math.Max(0, completed.Sequence - TraceCapacity),
                    SourceTraceError = sourceError,
                    ActivationTiming = activationTiming,
                    SecondaryRotationSupported = motionSupported, SecondaryRotationError = motionError,
                    SecondaryPositionSupported = motionSupported, SecondaryPositionError = motionError,
                    SourceScope = "AVFX quake types 6/9 construction/sample observations; secondary position and rotation producer-scoped or unscoped indexed vector arguments and native reset-call observations. A reset observation does not imply a new playback generation. No exact end, playback restart or camera winner is inferred. Raw float fields are IEEE-754 bits; native phase units are unverified.",
                    Sources = completed.Sources?.Snapshot(), Frames = frames },
                    new JsonSerializerOptions { Converters = { new JsonStringEnumConverter<ScreenEffectKind>(),
                        new JsonStringEnumConverter<ScreenEffectSourceRecording.EventKind>(),
                        new JsonStringEnumConverter<ScreenEffectSourceRecording.SourceFamily>(),
                        new JsonStringEnumConverter<ScreenEffectSourceRecording.EndEvidence>() } });
                // Reuse the existing atomic publisher and its twenty-session
                // retention so the next recording cannot erase this one.
                TraceArchive.Save(path, completed.StartedUtc, json);
                Volatile.Write(ref traceError, null);
            }
            catch (Exception exception) { Volatile.Write(ref traceError, "Camera timing log: " + exception.Message); }
        });
    }

    private void RecordCamera(ScreenEffectSample sample, Vector3 position, Vector3 rotation, ReadOnlySpan<float> vectors, CaptureContext epoch,
        float managerLevel, float scriptedLevel, ScriptedSample? scripted, long observedClock, string scriptedState)
    {
        var session = Volatile.Read(ref timing);
        if (session == null) return;
        if (!Monitor.TryEnter(session.Sync)) { Interlocked.Increment(ref session.Dropped); return; }
        try
        {
            if (!ReferenceEquals(session, Volatile.Read(ref timing))) return;
            static float Finite(float value) => float.IsFinite(value) ? value : 0;
            var tracker = epoch.Shake;
            session.Frames[session.Next] = new(++session.Sequence, sample.Timestamp, tracker.IntervalTicks,
                Finite(position.X), Finite(position.Y), Finite(position.Z), Finite(rotation.X), Finite(rotation.Y), Finite(rotation.Z),
                tracker.RawMagnitude, tracker.UnchangedFrames, sample.Level,
                output.ScreenEffectsActive ? ScreenEffectHaptics.Amplitude(sample, sample.Timestamp, output.ScreenSettings.ScreenStrength) : 0,
                output.UsesRumble ? "XInput" : "DualSense",
                0, 0, sample.Level <= 0 ? "quiet" : output.ScreenEffectsActive ? "awaiting-output" : "disabled")
            {
                Effects = sample.Effects, DominantEffect = sample.DominantEffect,
                PatternAmplitudeMultiplier = sample.AmplitudeMultiplier,
                ShakeLevel = Math.Max(managerLevel, scriptedLevel), ManagerShakeLevel = managerLevel, ScriptedShakeLevel = scriptedLevel,
                ScriptedRawMagnitude = epoch.ScriptedShake.RawMagnitude, ScriptedSampleClock = scripted?.Timestamp ?? 0,
                ScriptedObservedClock = observedClock, ScriptedSampleState = scriptedState,
                ScriptedPositionX = Finite(scripted?.Position.X ?? 0), ScriptedPositionY = Finite(scripted?.Position.Y ?? 0),
                ScriptedRotationX = Finite(scripted?.Rotation.X ?? 0), ScriptedRotationY = Finite(scripted?.Rotation.Y ?? 0),
                PrimaryPositionX = Finite(vectors[0]), PrimaryPositionY = Finite(vectors[1]), PrimaryPositionZ = Finite(vectors[2]),
                PrimaryRotationX = Finite(vectors[8]), PrimaryRotationY = Finite(vectors[9]), PrimaryRotationZ = Finite(vectors[10]),
                SecondaryPositionX = Finite(vectors[4]), SecondaryPositionY = Finite(vectors[5]), SecondaryPositionZ = Finite(vectors[6]),
                SecondaryRotationX = Finite(vectors[12]), SecondaryRotationY = Finite(vectors[13]), SecondaryRotationZ = Finite(vectors[14]),
            };
            session.Next = (session.Next + 1) % TraceCapacity;
            session.Count = Math.Min(session.Count + 1, TraceCapacity);
        }
        finally { Monitor.Exit(session.Sync); }
    }

    // Audio thread: record only a stable source snapshot and its FIRST successful
    // mix, so later blocks cannot make an on-time initial response look delayed.
    public void RecordOutput(NativeHapticOutput.ScreenMixSnapshot output)
    {
        var session = Volatile.Read(ref timing);
        if (session == null || output.SourceTimestamp == 0) return;
        if (!Monitor.TryEnter(session.Sync)) { Interlocked.Increment(ref session.Dropped); return; }
        try
        {
            if (!ReferenceEquals(session, Volatile.Read(ref timing))) return;
            if (session.Count == 0) return;
            var index = (session.Next - 1 + TraceCapacity) % TraceCapacity;
            var frame = session.Frames[index];
            if (frame.CaptureClock != output.SourceTimestamp) return;
            if (frame.FirstMixClock == 0)
            {
                if (output.Mixed) frame.FirstMixClock = output.Clock;
                frame.MixedGain = output.EffectiveLevel;
                frame.OutputState = output.State;
                frame.Output = output;
            }
            session.Frames[index] = frame;
        }
        finally { Monitor.Exit(session.Sync); }
    }

#endif

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref allowed, 0);
        Interlocked.Exchange(ref motionSession, null)?.Retire(Stopwatch.GetTimestamp());
        try { cameraUpdate.Dispose(); }
        finally
        {
            try { scriptedShake?.Dispose(); }
            finally
            {
                try { ResourceCleanup.Run(() => motionObserver?.Dispose()
#if BCEF_DIAGNOSTICS
                    , () => sourceObserver?.Dispose()
#endif
                    ); }
                finally
                {
                    Reset();
#if BCEF_DIAGNOSTICS
                    SetTracing(false);
                    traceSave.GetAwaiter().GetResult();
#endif
                }
            }
        }
    }

    private bool ValidObject(nint address)
    {
        if (address < 0x10000 || address > nint.MaxValue - 0x4800) return false;
        var table = Pointer(address);
        return table >= moduleStart && table < moduleEnd;
    }
    private static nint Pointer(nint address)
    {
        Span<byte> value = stackalloc byte[8];
        return Copy(address, value) ? (nint)BinaryPrimitives.ReadInt64LittleEndian(value) : 0;
    }
    internal static unsafe bool Copy(nint address, Span<byte> bytes)
    {
        if (address < 0x10000 || address > nint.MaxValue - bytes.Length) return false;
        fixed (byte* target = bytes)
            return ReadProcessMemory(-1, address, target, (nuint)bytes.Length, out var read) && read == (nuint)bytes.Length;
    }
}
