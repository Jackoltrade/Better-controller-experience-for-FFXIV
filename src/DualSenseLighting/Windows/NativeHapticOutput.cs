// SPDX-License-Identifier: GPL-3.0-only
// Native mixer bindings adapted from FFXIVClientStructs; see THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

/// <summary>Adds approved PCM to the game's existing vibration processing path.</summary>
internal sealed unsafe class NativeHapticOutput
{
    private readonly nint* submixes;
    private readonly nint mixIn;
    private readonly nint moduleStart, moduleEnd;
    private HapticSettings settings = new();
    // Screen output has its own immutable snapshot. Listener/spatial/SFX changes
    // must not attenuate it, restart its waveform or invalidate its current sample.
    private HapticSettings screenSettings = new();
    public bool UsesRumble => Volatile.Read(ref settings).RegularRumble;
    public SfxLoudness RumbleSignal { get; } = new();
    public SfxLoudness RumbleLeft { get; } = new();
    public SfxLoudness RumbleRight { get; } = new();
    public void ResetRumbleSignals()
    { RumbleSignal.SetActive(false); RumbleLeft.SetActive(false); RumbleRight.SetActive(false); }
    private readonly HapticImpactTracker impacts = new();
    private object impactEpoch = new();
    private sealed record ActiveImpact(object Epoch, HapticImpactCue Cue);
    private ActiveImpact? activeImpact;
    private HapticListenerPose? listener;
    private float[]? stereo8, stereo16;
    private long mixedBlocks, preservedNativeBlocks;
    private long spatialBlocks, unpositionedBlocks, spatialTime;
    private long suppressedBlocks, cutoffBlocks, biasedBlocks;
    private long footstepBlocks;
    private long bossBlocks, regularEnemyBlocks, hitReactionBlocks;
    private int enemyContext;
    private int footstepContext;
    private float spatialLeft, spatialRight, spatialDistance, spatialPan;
    private int available, faulted;
    private HapticEnableCue? enableCue;
    private sealed record ScreenFrame(ScreenEffectSample Sample, HapticSettings Settings);
    private ScreenFrame? screenFrame;
    private readonly ScreenEffectHaptics screenRenderer = new();
    private long screenBlocks, screenSuppressedBlocks;
    public readonly record struct ScreenMixSnapshot(long Clock, long SourceTimestamp, int InputCount,
        float GripPeak, float OtherPeak, bool InputInvalid, float Headroom, int LimitedFrames, float OutputPeak,
        float EffectiveLevel, bool Mixed, string State);
    // Read immediately after MixScreenEffects on that same audio thread.
    public ScreenMixSnapshot LastScreenMix { get; private set; }
    public long ScreenBlocks => Interlocked.Read(ref screenBlocks);
    public long ScreenSuppressedBlocks => Interlocked.Read(ref screenSuppressedBlocks);
    public bool ScreenEffectsActive => !Faulted && Volatile.Read(ref screenSettings) is { ScreenEffects: true, ScreenStrength: > 0 };
    public ScreenEffectSample? ScreenSample => Volatile.Read(ref screenFrame)?.Sample;

    public NativeHapticOutput(nint* submixes, nint mixIn, nint moduleStart, nint moduleEnd)
    { this.submixes = submixes; this.mixIn = mixIn; this.moduleStart = moduleStart; this.moduleEnd = moduleEnd; }
    public bool Active
    {
        get { var current = Volatile.Read(ref settings); return !Faulted && (current.Enabled && current.Strength > 0 || EnemiesActive || HitReactionsActive || FootstepsActive || ScreenEffectsActive); }
    }
    public bool FootstepsActive
    {
        get { var current = Volatile.Read(ref settings); return !Faulted && current.FootstepAmplifier &&
            current.FootstepMultiplier > 1 && Volatile.Read(ref footstepContext) != 0; }
    }
    public bool FootstepContextReady => Volatile.Read(ref footstepContext) != 0;
    public long FootstepBlocks => Interlocked.Read(ref footstepBlocks);
    public long BossBlocks => Interlocked.Read(ref bossBlocks);
    public long RegularEnemyBlocks => Interlocked.Read(ref regularEnemyBlocks);
    public bool EnemiesActive => !Faulted && Volatile.Read(ref enemyContext) != 0 &&
        Volatile.Read(ref settings) is { EnemiesEnabled: true, EnemyStrength: > 0 };
    public bool HitReactionsActive => !Faulted && Volatile.Read(ref enemyContext) != 0 &&
        Volatile.Read(ref settings) is { HitReactions: true, EnemyStrength: > 0 };
    public long HitReactionBlocks => Interlocked.Read(ref hitReactionBlocks);
    public bool Available => Volatile.Read(ref available) != 0;
    public bool Faulted => Volatile.Read(ref faulted) != 0;
    public long MixedBlocks => Interlocked.Read(ref mixedBlocks);
    public long PreservedNativeBlocks => Interlocked.Read(ref preservedNativeBlocks);
    public long SpatialBlocks => Interlocked.Read(ref spatialBlocks);
    public long UnpositionedBlocks => Interlocked.Read(ref unpositionedBlocks);
    public long SuppressedBlocks => Interlocked.Read(ref suppressedBlocks);
    public long CutoffBlocks => Interlocked.Read(ref cutoffBlocks);
    public long BiasedBlocks => Interlocked.Read(ref biasedBlocks);
    public HapticSettings CurrentSettings => Volatile.Read(ref settings);
    public HapticSettings ScreenSettings => Volatile.Read(ref screenSettings);
    public HapticImpactCue? ImpactCue
    {
        get
        {
            var current = Volatile.Read(ref activeImpact);
            return current != null && ReferenceEquals(current.Epoch, Volatile.Read(ref impactEpoch)) &&
                HapticImpactTracker.Fresh(current.Cue.Timestamp, Stopwatch.GetTimestamp()) ? current.Cue : null;
        }
    }
    public bool WantsSpatial
    {
        get
        {
            var current = Volatile.Read(ref settings);
            return !Faulted && (current.Spatial || current.DistanceFalloff) &&
                (current.Enabled && current.Strength > 0 || EnemiesActive || HitReactionsActive);
        }
    }
    public bool SpatialSupported => stereo8 != null && stereo16 != null;
    public HapticListenerPose? Listener
    {
        get { var pose = Volatile.Read(ref listener); return pose != null && Stopwatch.GetTimestamp() - pose.Timestamp <= Stopwatch.Frequency / 2 ? pose : null; }
    }
    public bool ListenerReady => Listener != null;
    public HapticSpatialFrame LastSpatial => Stopwatch.GetTimestamp() - Interlocked.Read(ref spatialTime) <= Stopwatch.Frequency / 3
        ? new(true, Volatile.Read(ref spatialPan), Volatile.Read(ref spatialDistance), Volatile.Read(ref spatialLeft), Volatile.Read(ref spatialRight)) : default;

    public void SetSpatialMatrices(float[] bed, float[] ambisonic) { stereo8 = bed; stereo16 = ambisonic; }
    public void SetListener(HapticListenerPose? pose) => Volatile.Write(ref listener, pose);
    public void SetEnemyContext(bool allowed)
    {
        if (Interlocked.Exchange(ref enemyContext, allowed ? 1 : 0) != (allowed ? 1 : 0)) ResetImpactCue();
    }
    public void SetFootstepContext(bool playerPresent, bool mounted)
    {
        var allowed = playerPresent && !mounted ? 1 : 0;
        if (Interlocked.Exchange(ref footstepContext, allowed) != allowed) ResetImpactCue();
    }
    public void ResetImpactCue()
    {
        Volatile.Write(ref impactEpoch, new object());
        Volatile.Write(ref activeImpact, null);
    }

    public void Configure(HapticSettings requested, bool foreground, bool attributionSupported, bool nativeRoutingSupported = true,
        bool useRumble = false, bool stereoRumble = false, bool nativeFootstepSupported = true)
    {
        var enabled = requested.Enabled && foreground && attributionSupported && nativeRoutingSupported && !Faulted;
        var footsteps = nativeFootstepSupported && !useRumble && requested.FootstepAmplifier && foreground && nativeRoutingSupported && !Faulted;
        var enemies = foreground && attributionSupported && nativeRoutingSupported && !Faulted && Volatile.Read(ref enemyContext) != 0;
        var boss = enemies && requested.BossEnabled;
        var regular = enemies && requested.RegularEnemyEnabled;
        var hits = enemies && requested.HitReactions;
        var screen = requested.ScreenEffects && foreground && nativeRoutingSupported && !Faulted;
        if (useRumble || !enabled && !footsteps && !boss && !regular && !hits && !screen) CancelEnableCue();
        var current = Volatile.Read(ref settings);
        var bluetooth = useRumble && stereoRumble;
        if (current.RegularRumble != useRumble || current.BluetoothRumble != bluetooth || current.OutputMode != requested.OutputMode ||
            current.RumbleController != requested.RumbleController || current.Enabled != enabled ||
            current.Strength != requested.Strength || current.Spatial != requested.Spatial ||
            current.DistanceFalloff != requested.DistanceFalloff || current.NearDistance != requested.NearDistance ||
            current.FarDistance != requested.FarDistance || current.ImpactBias != requested.ImpactBias ||
            current.AutomaticDistance != requested.AutomaticDistance || current.DistanceOrigin != requested.DistanceOrigin ||
            current.FollowImpact != requested.FollowImpact || current.FootstepAmplifier != footsteps ||
            current.FootstepMultiplier != requested.FootstepMultiplier || current.BossEnabled != boss ||
            current.RegularEnemyEnabled != regular || current.EnemyStrength != requested.EnemyStrength || current.HitReactions != hits ||
            current.RumbleStrength != requested.RumbleStrength || current.RumblePulseMilliseconds != requested.RumblePulseMilliseconds ||
            current.BluetoothMotorBalance != requested.BluetoothMotorBalance ||
            current.ScreenEffects != screen || current.ScreenStrength != requested.ScreenStrength)
        {
            var effective = requested with { Enabled = enabled, FootstepAmplifier = footsteps,
                BossEnabled = boss, RegularEnemyEnabled = regular, HitReactions = hits, RegularRumble = useRumble,
                BluetoothRumble = bluetooth, ScreenEffects = screen };
            if (current.ScreenEffects != screen || current.ScreenStrength != requested.ScreenStrength ||
                current.RegularRumble != useRumble || current.BluetoothRumble != bluetooth || current.OutputMode != requested.OutputMode ||
                current.RumbleController != requested.RumbleController)
            {
                Volatile.Write(ref screenSettings, effective);
                Volatile.Write(ref screenFrame, null);
            }
            Volatile.Write(ref settings, effective);
            Interlocked.Exchange(ref spatialTime, 0);
            ResetImpactCue();
            ResetRumbleSignals();
        }
        RumbleSignal.SetActive(useRumble && Active);
        RumbleLeft.SetActive(bluetooth && Active); RumbleRight.SetActive(bluetooth && Active);
        var epoch = Volatile.Read(ref impactEpoch);
        if (enabled && requested.Spatial && requested.FollowImpact &&
            impacts.TryTake(epoch, Stopwatch.GetTimestamp(), out var cue))
            Volatile.Write(ref activeImpact, new ActiveImpact(epoch, cue));
    }

    public bool HasEnableCue => Volatile.Read(ref enableCue) is { } cue && !cue.Finished(Stopwatch.GetTimestamp());

    public void RequestEnableCue()
    {
        if (!UsesRumble && Volatile.Read(ref settings).NeedsAudio && !Faulted)
            Volatile.Write(ref enableCue, new HapticEnableCue(Stopwatch.GetTimestamp() + Stopwatch.Frequency));
    }

    public void CancelEnableCue() => Volatile.Write(ref enableCue, null);

    // Framework publishes only current effects. Settings identity invalidates a
    // sample racing a disable, backend change or controller selection.
    public void SetScreenEffects(ScreenEffectSample? sample, HapticSettings expected)
    {
        if (sample == null || sample.Effects == ScreenEffectKind.None || !float.IsFinite(sample.Level) || sample.Level <= 0 || !ScreenEffectsActive ||
            !ReferenceEquals(expected, Volatile.Read(ref screenSettings)))
        { Volatile.Write(ref screenFrame, null); return; }
        Volatile.Write(ref screenFrame, new(sample, expected));
    }

    public float ScreenEffectLevel(long now)
    {
        var frame = Volatile.Read(ref screenFrame);
        if (frame == null || !ScreenEffectsActive ||
            !ReferenceEquals(frame.Settings, Volatile.Read(ref screenSettings))) return 0;
        return ScreenEffectHaptics.Response(ScreenEffectHaptics.Level(frame.Sample, now), frame.Settings.ScreenStrength);
    }

    public void MixScreenEffects(nint destination, bool foreground, bool layoutSupported, long now)
    {
        var frame = Volatile.Read(ref screenFrame);
        var current = Volatile.Read(ref screenSettings);
        var snapshot = new ScreenMixSnapshot(now, frame?.Sample.Timestamp ?? 0, -1, 0, 0, false, 1, 0, 0, 0, false, "quiet");
        void Record(string state, float level = 0, bool mixed = false)
        {
            LastScreenMix = snapshot with { State = state, EffectiveLevel = level, Mixed = mixed };
            if (!mixed && frame != null) Interlocked.Increment(ref screenSuppressedBlocks);
        }
        var guard = UsesRumble ? "rumble" : !foreground ? "background" : !layoutSupported ? "unsupported-audio" :
            !ScreenEffectsActive ? "disabled" : frame == null ? "quiet" :
            destination == 0 || destination != FindVibrationDestination() ? "invalid-destination" :
            !ReferenceEquals(frame.Settings, current) ? "settings-changed" : null;
        if (guard != null) { screenRenderer.Reset(); Record(guard); return; }
        var sample = frame!.Sample;
        var level = ScreenEffectHaptics.Response(ScreenEffectHaptics.Level(sample, now), current.ScreenStrength);
        if (level <= 0)
        {
            screenRenderer.Reset();
            Record(now < sample.Timestamp ? "future" : now - sample.Timestamp > ScreenEffectHaptics.FreshTicks ? "stale" :
                sample.ExpiresAt > 0 && now >= sample.ExpiresAt ? "expired" : "quiet");
            return;
        }
        // Read only the current native block. Source identities, input counts
        // and previous sounds never decide whether the camera may respond.
        var count = *(short*)(destination + 0x122);
        snapshot = snapshot with { InputCount = count };
        if (count < 0) { screenRenderer.Reset(); Record("invalid-native-input", level); return; }
        ReadOnlySpan<float> nativeInput = default;
        if (count > 0)
        {
            var input = *(float**)(destination + 0x80);
            if (input == null || *(int*)(destination + 0x88) < ScreenEffectHaptics.Frames)
            { screenRenderer.Reset(); Record("invalid-native-input", level); return; }
            nativeInput = new(input, ScreenEffectHaptics.Frames * ScreenEffectHaptics.Channels);
            var gripPeak = 0f; var otherPeak = 0f;
            for (var index = 0; index < nativeInput.Length; index++)
            {
                var value = nativeInput[index];
                if (!float.IsFinite(value))
                {
                    snapshot = snapshot with { InputInvalid = true };
                    screenRenderer.Reset(); Record("invalid-native-input", level); return;
                }
                if (index % ScreenEffectHaptics.Channels < 2) gripPeak = Math.Max(gripPeak, Math.Abs(value));
                else otherPeak = Math.Max(otherPeak, Math.Abs(value));
            }
            snapshot = snapshot with { GripPeak = gripPeak, OtherPeak = otherPeak };
        }
        Span<float> samples = stackalloc float[ScreenEffectHaptics.Frames * ScreenEffectHaptics.Channels];
        var rendered = screenRenderer.Render(samples, level, sample.Texture, nativeInput);
        snapshot = snapshot with { Headroom = screenRenderer.MinimumHeadroom, LimitedFrames = screenRenderer.LimitedFrames,
            OutputPeak = screenRenderer.OutputPeak };
        if (!rendered) { Record("headroom", level); return; }
        if (!ReferenceEquals(current, Volatile.Read(ref screenSettings)) || !ReferenceEquals(frame, Volatile.Read(ref screenFrame)))
        { Record("settings-changed", level); return; }
        fixed (float* pcm = samples)
            ((delegate* unmanaged<nint, float*, uint, float, uint, void>)mixIn)(destination, pcm, ScreenEffectHaptics.Frames, 1, 0);
        Interlocked.Increment(ref screenBlocks);
        Record("mixed", level, true);
    }

    public void MixEnableCue(nint destination, bool foreground, bool layoutSupported)
    {
        var cue = Volatile.Read(ref enableCue);
        if (cue == null) return;
        var current = Volatile.Read(ref settings);
        if (current.RegularRumble || !foreground || !layoutSupported || !current.NeedsAudio || Faulted)
        { CancelEnableCue(); return; }
        if (destination == 0 || destination != FindVibrationDestination()) return;
        Span<float> samples = stackalloc float[HapticEnableCue.FramesPerBlock * HapticEnableCue.Channels];
        var now = Stopwatch.GetTimestamp();
        var rendered = cue.Render(samples, now);
        // A disable/new request can invalidate this block while it is being prepared.
        if (!ReferenceEquals(cue, Volatile.Read(ref enableCue)) ||
            !ReferenceEquals(current, Volatile.Read(ref settings))) return;
        if (rendered)
            fixed (float* pcm = samples)
                ((delegate* unmanaged<nint, float*, uint, float, uint, void>)mixIn)(destination, pcm, HapticEnableCue.FramesPerBlock, 1, 0);
        if (cue.Finished(now)) Interlocked.CompareExchange(ref enableCue, null, cue);
    }

    private nint FindVibrationDestination()
    {
        if (submixes == null || mixIn == 0) return 0;
        // Re-resolve the current graph on the audio thread; retain no device pointer.
        for (var slot = 0; slot < 15; slot++)
        {
            var destination = submixes[slot];
            if (destination == 0 || !new ReadOnlySpan<byte>((void*)(destination + 0x13E), 10).SequenceEqual("Vibration\0"u8)) continue;
            var table = *(nint*)destination;
            return table >= moduleStart && table <= moduleEnd - 16 * 8 && *(nint*)(table + 15 * 8) == mixIn &&
                *(uint*)(destination + 0x138) == 0 && *(int*)(destination + 0xE0) is >= 1 and <= 5 ? destination : 0;
        }
        return 0;
    }

    public readonly record struct Frame(nint Destination, HapticSettings? Settings, float Gain,
        HapticSpatialFrame Spatial = default, HapticRoute Route = HapticRoute.None,
        Vector3? Source = null, int SoundId = 0, object? Epoch = null, bool Biased = false,
        EnemySoundKind Enemy = EnemySoundKind.None, bool HitReaction = false,
        bool Rumble = false, uint RumbleGeneration = 0, uint LeftGeneration = 0, uint RightGeneration = 0)
    {
        public bool HasOutput => Destination != 0 || Rumble;
    }

    public Frame Prepare(GameAudioLayout.SourceVoice* voice, bool owned, byte bus, uint flags, Vector3? position = null, int soundId = 0,
        EnemySoundKind enemy = EnemySoundKind.None, bool hitReaction = false, bool arenaEffect = false)
    {
        var current = Volatile.Read(ref settings);
        if (owned || !EnemiesActive) enemy = EnemySoundKind.None;
        var combatGain = current.AdditionalGain(owned, bus, flags, false, enemy, hitReaction, arenaEffect);
        var footstepCandidate = bus == 6 && FootstepsActive;
        if (combatGain <= 0 && !footstepCandidate)
        {
            if ((current.Enabled && owned || enemy != EnemySoundKind.None) && (flags & 0xE) != 0)
            { Interlocked.Increment(ref preservedNativeBlocks); return new(0, current, 0, Route: HapticRoute.Native); }
            return default;
        }
        var pose = Listener;
        var epoch = Volatile.Read(ref impactEpoch);
        // Resolve on the game's audio thread, using its current graph each time.
        // No heap pointer is retained across an audio-device change or replug.
        var rumble = current.RegularRumble;
        var destination = rumble ? 0 : FindVibrationDestination();
        if (destination != 0 || rumble)
        {
            Volatile.Write(ref available, 1);
            if (!rumble && GameAudioLayout.TryGetSendGain(voice, destination, out var nativeGain))
            {
                Interlocked.Increment(ref preservedNativeBlocks);
                var extraGain = footstepCandidate ? current.NativeFootstepExtraGain(bus, true, nativeGain) : 0;
                return extraGain > 0 ? new(destination, current, extraGain, Route: HapticRoute.NativeFootstep,
                    SoundId: soundId, Epoch: epoch) : new(0, current, 0, Route: HapticRoute.Native);
            }
            if (combatGain <= 0) return default; // A footstep without a native vibration send stays unmodified.
            if (current.DistanceFalloff && (!SpatialSupported || !position.HasValue ||
                !HapticSpatializer.ValidPosition(position.Value) || pose == null))
            {
                Interlocked.Increment(ref suppressedBlocks);
                return new(0, current, 0, Route: !SpatialSupported ? HapticRoute.SpatialUnavailable :
                    !position.HasValue || !HapticSpatializer.ValidPosition(position.Value) ? HapticRoute.MissingPosition : HapticRoute.MissingListener);
            }
            var spatial = (current.Spatial || current.DistanceFalloff) && SpatialSupported && position.HasValue
                ? HapticSpatializer.Evaluate(current, pose, position.Value, Stopwatch.GetTimestamp()) : default;
            if (current.DistanceFalloff && !spatial.Valid)
            { Interlocked.Increment(ref suppressedBlocks); return new(0, current, 0, Route: HapticRoute.MissingListener); }
            var biased = false;
            if (owned && !hitReaction && current.Spatial && spatial.Valid && pose != null && position.HasValue && ImpactCue is { } impact)
            {
                var adjusted = HapticSpatializer.BiasTowardImpact(current, pose, position.Value, spatial, impact.Position);
                biased = adjusted.Pan != spatial.Pan;
                spatial = adjusted;
            }
            if (spatial.Valid && spatial.Left <= 0 && spatial.Right <= 0)
            {
                Interlocked.Increment(ref cutoffBlocks);
                return new(0, current, 0, spatial, HapticRoute.DistanceCutoff);
            }
            return new(destination, current, combatGain, spatial, spatial.Valid ? HapticRoute.Spatial : HapticRoute.Original,
                position, soundId, epoch, biased, enemy, hitReaction, rumble, RumbleSignal.Generation,
                RumbleLeft.Generation, RumbleRight.Generation);
        }
        Volatile.Write(ref available, 0);
        return default;
    }

    public void Mix(Frame frame, float* scratch, int sampleCount, uint format)
    {
        if (!frame.HasOutput || frame.Rumble != UsesRumble || scratch == null || sampleCount <= 0 || frame.Gain <= 0 ||
            !ReferenceEquals(frame.Settings, Volatile.Read(ref settings)) ||
            !ReferenceEquals(frame.Epoch, Volatile.Read(ref impactEpoch)) || !Active ||
            frame.Enemy != EnemySoundKind.None && !EnemiesActive ||
            frame.HitReaction && !HitReactionsActive ||
            frame.Route == HapticRoute.NativeFootstep && !FootstepsActive) return;
        try
        {
            // Validate the complete block before adding it. MixIn reads the same
            // processed samples as native sends; it does not modify this buffer.
            var rms = SfxLoudness.MeasureRms(new ReadOnlySpan<float>(scratch, sampleCount));
            if (rms <= 0) return;
            if (frame.Rumble)
            {
                Span<float> level = stackalloc float[1];
                level[0] = RumbleMotors.Measure(rms, frame.Gain, frame.Spatial);
                RumbleSignal.Observe(level, frame.RumbleGeneration);
                if (frame.Settings?.BluetoothRumble == true)
                {
                    var sides = RumbleMotors.MeasureStereo(rms, frame.Gain, frame.Spatial);
                    level[0] = sides.Left; RumbleLeft.Observe(level, frame.LeftGeneration);
                    level[0] = sides.Right; RumbleRight.Observe(level, frame.RightGeneration);
                }
            }
            if (frame.Spatial.Valid)
            {
                if (!frame.Rumble)
                {
                    var output = stackalloc float[2048];
                    var matrix = sampleCount == 4096 ? stereo16 : stereo8;
                    if (matrix == null || !HapticSpatializer.Render(new ReadOnlySpan<float>(scratch, sampleCount), matrix,
                        new Span<float>(output, 2048), frame.Spatial)) return;
                    ((delegate* unmanaged<nint, float*, uint, float, uint, void>)mixIn)(frame.Destination, output, 256, frame.Gain, 0);
                }
                Interlocked.Increment(ref spatialBlocks);
                if (frame.Biased) Interlocked.Increment(ref biasedBlocks);
                if (frame.Enemy == EnemySoundKind.None && !frame.HitReaction && frame.Settings is { Spatial: true, FollowImpact: true } && frame.Source is { } point && Listener is { } pose)
                {
                    var offset = point - pose.Origin;
                    if (new Vector2(offset.X, offset.Z).Length() >= 0.75f)
                        impacts.Observe(frame.Epoch!, frame.SoundId, point, rms, Stopwatch.GetTimestamp());
                }
                Volatile.Write(ref spatialLeft, frame.Spatial.Left * frame.Gain);
                Volatile.Write(ref spatialRight, frame.Spatial.Right * frame.Gain);
                Volatile.Write(ref spatialDistance, frame.Spatial.Distance);
                Volatile.Write(ref spatialPan, frame.Spatial.Pan);
                Interlocked.Exchange(ref spatialTime, Stopwatch.GetTimestamp());
            }
            else
            {
                if (!frame.Rumble)
                    ((delegate* unmanaged<nint, float*, uint, float, uint, void>)mixIn)(frame.Destination, scratch, 256, frame.Gain, format);
                if (frame.Route != HapticRoute.NativeFootstep && frame.Settings?.Spatial == true)
                    Interlocked.Increment(ref unpositionedBlocks);
            }
            if (frame.Route == HapticRoute.NativeFootstep) Interlocked.Increment(ref footstepBlocks);
            else Interlocked.Increment(ref mixedBlocks);
            if (frame.Enemy == EnemySoundKind.Boss) Interlocked.Increment(ref bossBlocks);
            else if (frame.Enemy == EnemySoundKind.Regular) Interlocked.Increment(ref regularEnemyBlocks);
            if (frame.HitReaction) Interlocked.Increment(ref hitReactionBlocks);
        }
        catch { Interlocked.Exchange(ref faulted, 1); Configure(new HapticSettings(), false, false); }
    }
}
