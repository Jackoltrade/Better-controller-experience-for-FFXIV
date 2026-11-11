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
    public StereoRumbleSignal RumbleStereo { get; } = new();
    public void ResetRumbleSignals()
    { RumbleSignal.SetActive(false); RumbleStereo.SetActive(false); }
    private readonly HapticImpactTracker impacts = new();
    private object impactEpoch = new();
    private sealed record ActiveImpact(object Epoch, HapticImpactCue Cue);
    private ActiveImpact? activeImpact;
    private HapticListenerPose? listener;
    private float[]? stereo8, stereo16;
#if BCEF_DIAGNOSTICS
    private long mixedBlocks, preservedNativeBlocks;
    private long spatialBlocks, unpositionedBlocks, spatialTime;
    private long suppressedBlocks, cutoffBlocks, biasedBlocks;
    private long footstepBlocks;
    private long bossBlocks, regularEnemyBlocks, hitReactionBlocks;
#endif
    private int enemyContext;
    private int footstepContext;
#if BCEF_DIAGNOSTICS
    private float spatialLeft, spatialRight, spatialDistance, spatialPan;
#endif
    private int available, faulted;
    private HapticEnableCue? enableCue;
    private sealed record ScreenFrame(ScreenEffectSample Sample, HapticSettings Settings);
    private ScreenFrame? screenFrame;
    private readonly ScreenEffectHaptics screenRenderer = new();
#if BCEF_DIAGNOSTICS
    private long screenBlocks, screenSuppressedBlocks;
    public readonly record struct ScreenMixSnapshot(long Clock, long SourceTimestamp, int InputCount,
        float GripPeak, float OtherPeak, bool InputInvalid, float Headroom, int LimitedFrames, float OutputPeak,
        float EffectiveLevel, bool Mixed, string State);
    // Read immediately after MixScreenEffects on that same audio thread.
    public ScreenMixSnapshot LastScreenMix { get; private set; }
    public long ScreenBlocks => Interlocked.Read(ref screenBlocks);
    public long ScreenSuppressedBlocks => Interlocked.Read(ref screenSuppressedBlocks);
#endif
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
#if BCEF_DIAGNOSTICS
    public bool FootstepContextReady => Volatile.Read(ref footstepContext) != 0;
    public long FootstepBlocks => Interlocked.Read(ref footstepBlocks);
    public long BossBlocks => Interlocked.Read(ref bossBlocks);
    public long RegularEnemyBlocks => Interlocked.Read(ref regularEnemyBlocks);
#endif
    public bool EnemiesActive => !Faulted && Volatile.Read(ref enemyContext) != 0 &&
        Volatile.Read(ref settings) is { EnemiesEnabled: true, EnemyStrength: > 0 };
    public bool HitReactionsActive => !Faulted && Volatile.Read(ref enemyContext) != 0 &&
        Volatile.Read(ref settings) is { HitReactions: true, EnemyStrength: > 0 };
#if BCEF_DIAGNOSTICS
    public long HitReactionBlocks => Interlocked.Read(ref hitReactionBlocks);
#endif
    public bool Available => Volatile.Read(ref available) != 0;
    public bool Faulted => Volatile.Read(ref faulted) != 0;
#if BCEF_DIAGNOSTICS
    public long MixedBlocks => Interlocked.Read(ref mixedBlocks);
    public long PreservedNativeBlocks => Interlocked.Read(ref preservedNativeBlocks);
    public long SpatialBlocks => Interlocked.Read(ref spatialBlocks);
    public long UnpositionedBlocks => Interlocked.Read(ref unpositionedBlocks);
    public long SuppressedBlocks => Interlocked.Read(ref suppressedBlocks);
    public long CutoffBlocks => Interlocked.Read(ref cutoffBlocks);
    public long BiasedBlocks => Interlocked.Read(ref biasedBlocks);
#endif
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
#if BCEF_DIAGNOSTICS
    public bool ListenerReady => Listener != null;
    public HapticSpatialFrame LastSpatial => Stopwatch.GetTimestamp() - Interlocked.Read(ref spatialTime) <= Stopwatch.Frequency / 3
        ? new(true, Volatile.Read(ref spatialPan), Volatile.Read(ref spatialDistance), Volatile.Read(ref spatialLeft), Volatile.Read(ref spatialRight)) : default;

#endif
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
        var screenChanged = ScreenOutputKey(current) !=
            (screen, requested.ScreenStrength, useRumble, bluetooth, requested.OutputMode, requested.RumbleController);
        if (screenChanged || ShapeKey(current) != ShapeKey(requested) ||
            (current.Enabled, current.FootstepAmplifier, current.BossEnabled, current.RegularEnemyEnabled, current.HitReactions) !=
            (enabled, footsteps, boss, regular, hits))
        {
            var effective = requested with { Enabled = enabled, FootstepAmplifier = footsteps,
                BossEnabled = boss, RegularEnemyEnabled = regular, HitReactions = hits, RegularRumble = useRumble,
                BluetoothRumble = bluetooth, ScreenEffects = screen };
            if (screenChanged)
            {
                Volatile.Write(ref screenSettings, effective);
                Volatile.Write(ref screenFrame, null);
            }
            Volatile.Write(ref settings, effective);
#if BCEF_DIAGNOSTICS
            Interlocked.Exchange(ref spatialTime, 0);
#endif
            ResetImpactCue();
            ResetRumbleSignals();
        }
        RumbleSignal.SetActive(useRumble && Active);
        RumbleStereo.SetActive(bluetooth && Active);
        var epoch = Volatile.Read(ref impactEpoch);
        if (enabled && requested.Spatial && requested.FollowImpact &&
            impacts.TryTake(epoch, Stopwatch.GetTimestamp(), out var cue))
            Volatile.Write(ref activeImpact, new ActiveImpact(epoch, cue));
    }

    // Screen timing has an independent snapshot: player/spatial/attribution
    // changes reset combat frames without restarting the screen waveform.
    private static (bool, float, bool, bool, HapticOutputMode, int) ScreenOutputKey(HapticSettings value) =>
        (value.ScreenEffects, value.ScreenStrength, value.RegularRumble, value.BluetoothRumble, value.OutputMode, value.RumbleController);

    private static (float, bool, bool, float, float, float, bool, HapticDistanceOrigin, bool, float, float, float, int, float)
        ShapeKey(HapticSettings value) =>
        (value.Strength, value.Spatial, value.DistanceFalloff, value.NearDistance, value.FarDistance, value.ImpactBias,
            value.AutomaticDistance, value.DistanceOrigin, value.FollowImpact, value.FootstepMultiplier,
            value.EnemyStrength, value.RumbleStrength, value.RumblePulseMilliseconds, value.BluetoothMotorBalance);

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
        return ScreenEffectHaptics.Amplitude(frame.Sample, now, frame.Settings.ScreenStrength);
    }

    public void MixScreenEffects(nint destination, bool foreground, bool layoutSupported, long now)
    {
        var frame = Volatile.Read(ref screenFrame);
        var current = Volatile.Read(ref screenSettings);
#if BCEF_DIAGNOSTICS
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
#endif
        if (UsesRumble || !foreground || !layoutSupported || !ScreenEffectsActive || frame == null ||
            destination == 0 || destination != FindVibrationDestination() || !ReferenceEquals(frame.Settings, current))
        {
            screenRenderer.Reset();
#if BCEF_DIAGNOSTICS
            Record(guard!);
#endif
            return;
        }
        var sample = frame.Sample;
        var level = ScreenEffectHaptics.Amplitude(sample, now, current.ScreenStrength);
        if (level <= 0)
        {
            screenRenderer.Reset();
#if BCEF_DIAGNOSTICS
            Record(now < sample.Timestamp ? "future" : now - sample.Timestamp > ScreenEffectHaptics.FreshTicks ? "stale" :
                sample.ExpiresAt > 0 && now >= sample.ExpiresAt ? "expired" : "quiet");
#endif
            return;
        }
        // Validate the current native block even when diagnostic peaks are compiled out.
        var count = *(short*)(destination + 0x122);
#if BCEF_DIAGNOSTICS
        snapshot = snapshot with { InputCount = count };
#endif
        if (count < 0)
        {
            screenRenderer.Reset();
#if BCEF_DIAGNOSTICS
            Record("invalid-native-input", level);
#endif
            return;
        }
        ReadOnlySpan<float> nativeInput = default;
        if (count > 0)
        {
            var input = *(float**)(destination + 0x80);
            if (input == null || *(int*)(destination + 0x88) < ScreenEffectHaptics.Frames)
            {
                screenRenderer.Reset();
#if BCEF_DIAGNOSTICS
                Record("invalid-native-input", level);
#endif
                return;
            }
            nativeInput = new(input, ScreenEffectHaptics.Frames * ScreenEffectHaptics.Channels);
#if BCEF_DIAGNOSTICS
            var gripPeak = 0f; var otherPeak = 0f;
#endif
            for (var index = 0; index < nativeInput.Length; index++)
            {
                var value = nativeInput[index];
                if (!float.IsFinite(value))
                {
                    screenRenderer.Reset();
#if BCEF_DIAGNOSTICS
                    snapshot = snapshot with { InputInvalid = true };
                    Record("invalid-native-input", level);
#endif
                    return;
                }
#if BCEF_DIAGNOSTICS
                if (index % ScreenEffectHaptics.Channels < 2) gripPeak = Math.Max(gripPeak, Math.Abs(value));
                else otherPeak = Math.Max(otherPeak, Math.Abs(value));
#endif
            }
#if BCEF_DIAGNOSTICS
            snapshot = snapshot with { GripPeak = gripPeak, OtherPeak = otherPeak };
#endif
        }
        Span<float> samples = stackalloc float[ScreenEffectHaptics.Frames * ScreenEffectHaptics.Channels];
        var rendered = screenRenderer.Render(samples, level, sample.Texture, nativeInput);
#if BCEF_DIAGNOSTICS
        snapshot = snapshot with { Headroom = screenRenderer.MinimumHeadroom, LimitedFrames = screenRenderer.LimitedFrames,
            OutputPeak = screenRenderer.OutputPeak };
#endif
        if (!rendered)
        {
#if BCEF_DIAGNOSTICS
            Record("headroom", level);
#endif
            return;
        }
        if (!ReferenceEquals(current, Volatile.Read(ref screenSettings)) || !ReferenceEquals(frame, Volatile.Read(ref screenFrame)))
        {
#if BCEF_DIAGNOSTICS
            Record("settings-changed", level);
#endif
            return;
        }
        fixed (float* pcm = samples)
            ((delegate* unmanaged<nint, float*, uint, float, uint, void>)mixIn)(destination, pcm, ScreenEffectHaptics.Frames, 1, 0);
#if BCEF_DIAGNOSTICS
        Interlocked.Increment(ref screenBlocks);
        Record("mixed", level, true);
#endif
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
        bool Rumble = false, uint RumbleGeneration = 0, StereoRumbleSignal.Epoch? StereoEpoch = null)
    {
        public bool HasOutput => Destination != 0 || Rumble;
    }

    public Frame Prepare(GameAudioLayout.SourceVoice* voice, bool owned, byte bus, uint flags, Vector3? position = null, int soundId = 0,
        EnemySoundKind enemy = EnemySoundKind.None, bool hitReaction = false, bool arenaEffect = false,
        Vector3? distancePosition = null, bool allowUnlocatedExecution = false)
    {
        var current = Volatile.Read(ref settings);
        if (owned || !EnemiesActive) enemy = EnemySoundKind.None;
        var combatGain = current.AdditionalGain(owned, bus, flags, false, enemy, hitReaction, arenaEffect);
        var footstepCandidate = bus == 6 && FootstepsActive;
        if (combatGain <= 0 && !footstepCandidate)
        {
            if ((current.Enabled && owned || enemy != EnemySoundKind.None) && (flags & 0xE) != 0)
            {
#if BCEF_DIAGNOSTICS
                Interlocked.Increment(ref preservedNativeBlocks);
#endif
 return new(0, current, 0, Route: HapticRoute.Native); }
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

#if BCEF_DIAGNOSTICS
                Interlocked.Increment(ref preservedNativeBlocks);
#endif

                var extraGain = footstepCandidate ? current.NativeFootstepExtraGain(bus, true, nativeGain) : 0;
                return extraGain > 0 ? new(destination, current, extraGain, Route: HapticRoute.NativeFootstep,
                    SoundId: soundId, Epoch: epoch) : new(0, current, 0, Route: HapticRoute.Native);
            }
            if (combatGain <= 0) return default; // A footstep without a native vibration send stays unmodified.
            position ??= distancePosition;
            if (current.DistanceFalloff && (!SpatialSupported || !position.HasValue ||
                !HapticSpatializer.ValidPosition(position.Value) || pose == null))
            {

#if BCEF_DIAGNOSTICS
                Interlocked.Increment(ref suppressedBlocks);
#endif

                return new(0, current, 0, Route: !SpatialSupported ? HapticRoute.SpatialUnavailable :
                    !position.HasValue || !HapticSpatializer.ValidPosition(position.Value) ? HapticRoute.MissingPosition : HapticRoute.MissingListener);
            }
            // Self-targeted animation audio has no recoverable contact point.
            // Preserve its strength; proven impact positions always retain fade.
            var animation = current.DistanceFalloff && allowUnlocatedExecution && !distancePosition.HasValue;
            var spatial = (current.Spatial || current.DistanceFalloff) && SpatialSupported && position.HasValue
                ? HapticSpatializer.Evaluate(current, pose, position.Value, Stopwatch.GetTimestamp(), distancePosition, animation) : default;
            if (current.DistanceFalloff && !spatial.Valid)
            {
#if BCEF_DIAGNOSTICS
                Interlocked.Increment(ref suppressedBlocks);
#endif
 return new(0, current, 0, Route: HapticRoute.MissingListener); }
            var biased = false;
            if (owned && !hitReaction && current.Spatial && spatial.Valid && pose != null && position.HasValue && ImpactCue is { } impact)
            {
                var adjusted = HapticSpatializer.BiasTowardImpact(current, pose, position.Value, spatial, impact.Position);
                biased = adjusted.Pan != spatial.Pan;
                spatial = adjusted;
            }
            if (spatial.Valid && spatial.Left <= 0 && spatial.Right <= 0)
            {

#if BCEF_DIAGNOSTICS
                Interlocked.Increment(ref cutoffBlocks);
#endif

                return new(0, current, 0, spatial, HapticRoute.DistanceCutoff);
            }
            return new(destination, current, combatGain, spatial, spatial.Valid
                ? animation ? HapticRoute.Animation : HapticRoute.Spatial : HapticRoute.Original,
                position, soundId, epoch, biased, enemy, hitReaction, rumble, RumbleSignal.Generation,
                RumbleStereo.CurrentEpoch);
        }
        Volatile.Write(ref available, 0);
        return default;
    }

    public float? Mix(Frame frame, float* scratch, int sampleCount, uint format)
    {
        if (!frame.HasOutput || frame.Rumble != UsesRumble || scratch == null || sampleCount <= 0 || frame.Gain <= 0 ||
            !ReferenceEquals(frame.Settings, Volatile.Read(ref settings)) ||
            !ReferenceEquals(frame.Epoch, Volatile.Read(ref impactEpoch)) || !Active ||
            frame.Enemy != EnemySoundKind.None && !EnemiesActive ||
            frame.HitReaction && !HitReactionsActive ||
            frame.Route == HapticRoute.NativeFootstep && !FootstepsActive) return null;
        float? measured = null;
        try
        {
            // Validate the complete block before adding it. MixIn reads the same
            // processed samples as native sends; it does not modify this buffer.
            var rms = SfxLoudness.MeasureRms(new ReadOnlySpan<float>(scratch, sampleCount));
            measured = rms;
            if (rms <= 0) return rms;
            if (frame.Rumble)
            {
                Span<float> level = stackalloc float[1];
                level[0] = RumbleMotors.Measure(rms, frame.Gain, frame.Spatial);
                RumbleSignal.Observe(level, frame.RumbleGeneration);
                if (frame.Settings?.BluetoothRumble == true)
                {
                    var sides = RumbleMotors.MeasureStereo(rms, frame.Gain, frame.Spatial);
                    if (frame.StereoEpoch is { } epoch) RumbleStereo.Observe(sides.Left, sides.Right, epoch);
                }
            }
            if (frame.Spatial.Valid)
            {
                if (!frame.Rumble)
                {
                    var output = stackalloc float[2048];
                    var matrix = sampleCount == 4096 ? stereo16 : stereo8;
                    if (matrix == null || !HapticSpatializer.Render(new ReadOnlySpan<float>(scratch, sampleCount), matrix,
                        new Span<float>(output, 2048), frame.Spatial)) return rms;
                    ((delegate* unmanaged<nint, float*, uint, float, uint, void>)mixIn)(frame.Destination, output, 256, frame.Gain, 0);
                }
#if BCEF_DIAGNOSTICS
                Interlocked.Increment(ref spatialBlocks);
                if (frame.Biased) Interlocked.Increment(ref biasedBlocks);
#endif
                if (frame.Enemy == EnemySoundKind.None && !frame.HitReaction && frame.Settings is { Spatial: true, FollowImpact: true } && frame.Source is { } point && Listener is { } pose)
                {
                    var offset = point - pose.Origin;
                    if (new Vector2(offset.X, offset.Z).Length() >= 0.75f)
                        impacts.Observe(frame.Epoch!, frame.SoundId, point, rms, Stopwatch.GetTimestamp());
                }
#if BCEF_DIAGNOSTICS
                Volatile.Write(ref spatialLeft, frame.Spatial.Left * frame.Gain);
                Volatile.Write(ref spatialRight, frame.Spatial.Right * frame.Gain);
                Volatile.Write(ref spatialDistance, frame.Spatial.Distance);
                Volatile.Write(ref spatialPan, frame.Spatial.Pan);
                Interlocked.Exchange(ref spatialTime, Stopwatch.GetTimestamp());
#endif
            }
            else
            {
                if (!frame.Rumble)
                    ((delegate* unmanaged<nint, float*, uint, float, uint, void>)mixIn)(frame.Destination, scratch, 256, frame.Gain, format);
#if BCEF_DIAGNOSTICS
                if (frame.Route != HapticRoute.NativeFootstep && frame.Settings?.Spatial == true)
                    Interlocked.Increment(ref unpositionedBlocks);
#endif
            }
#if BCEF_DIAGNOSTICS
            if (frame.Route == HapticRoute.NativeFootstep) Interlocked.Increment(ref footstepBlocks);
            else Interlocked.Increment(ref mixedBlocks);
            if (frame.Enemy == EnemySoundKind.Boss) Interlocked.Increment(ref bossBlocks);
            else if (frame.Enemy == EnemySoundKind.Regular) Interlocked.Increment(ref regularEnemyBlocks);
            if (frame.HitReaction) Interlocked.Increment(ref hitReactionBlocks);
#endif
        }
        catch { Interlocked.Exchange(ref faulted, 1); Configure(new HapticSettings(), false, false); }
        return measured;
    }
}
