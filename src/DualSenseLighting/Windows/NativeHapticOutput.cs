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
    private int footstepContext;
    private float spatialLeft, spatialRight, spatialDistance, spatialPan;
    private int available, faulted;
    private HapticEnableCue? enableCue;

    public NativeHapticOutput(nint* submixes, nint mixIn, nint moduleStart, nint moduleEnd)
    { this.submixes = submixes; this.mixIn = mixIn; this.moduleStart = moduleStart; this.moduleEnd = moduleEnd; }
    public bool Active
    {
        get { var current = Volatile.Read(ref settings); return !Faulted && (current.Enabled && current.Strength > 0 || FootstepsActive); }
    }
    public bool FootstepsActive
    {
        get { var current = Volatile.Read(ref settings); return !Faulted && current.FootstepAmplifier &&
            current.FootstepMultiplier > 1 && Volatile.Read(ref footstepContext) != 0; }
    }
    public bool FootstepContextReady => Volatile.Read(ref footstepContext) != 0;
    public long FootstepBlocks => Interlocked.Read(ref footstepBlocks);
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
    public HapticImpactCue? ImpactCue
    {
        get
        {
            var current = Volatile.Read(ref activeImpact);
            return current != null && ReferenceEquals(current.Epoch, Volatile.Read(ref impactEpoch)) &&
                HapticImpactTracker.Fresh(current.Cue.Timestamp, Stopwatch.GetTimestamp()) ? current.Cue : null;
        }
    }
    public bool WantsSpatial => Active && Volatile.Read(ref settings) is { Enabled: true, Strength: > 0, Spatial: true };
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

    public void Configure(HapticSettings requested, bool foreground, bool attributionSupported, bool nativeRoutingSupported = true)
    {
        var enabled = requested.Enabled && foreground && attributionSupported && nativeRoutingSupported && !Faulted;
        var footsteps = requested.FootstepAmplifier && foreground && nativeRoutingSupported && !Faulted;
        if (!enabled) CancelEnableCue();
        var current = Volatile.Read(ref settings);
        if (current.Enabled != enabled || current.Strength != requested.Strength || current.Spatial != requested.Spatial ||
            current.DistanceFalloff != requested.DistanceFalloff || current.NearDistance != requested.NearDistance ||
            current.FarDistance != requested.FarDistance || current.ImpactBias != requested.ImpactBias ||
            current.AutomaticDistance != requested.AutomaticDistance || current.DistanceOrigin != requested.DistanceOrigin ||
            current.FollowImpact != requested.FollowImpact || current.FootstepAmplifier != footsteps ||
            current.FootstepMultiplier != requested.FootstepMultiplier)
        {
            Volatile.Write(ref settings, requested with { Enabled = enabled, FootstepAmplifier = footsteps });
            Interlocked.Exchange(ref spatialTime, 0);
            ResetImpactCue();
        }
        var epoch = Volatile.Read(ref impactEpoch);
        if (enabled && requested.Spatial && requested.FollowImpact &&
            impacts.TryTake(epoch, Stopwatch.GetTimestamp(), out var cue))
            Volatile.Write(ref activeImpact, new ActiveImpact(epoch, cue));
    }

    public bool HasEnableCue => Volatile.Read(ref enableCue) is { } cue && !cue.Finished(Stopwatch.GetTimestamp());

    public void RequestEnableCue()
    {
        if (Volatile.Read(ref settings).Enabled && !Faulted)
            Volatile.Write(ref enableCue, new HapticEnableCue(Stopwatch.GetTimestamp() + Stopwatch.Frequency));
    }

    public void CancelEnableCue() => Volatile.Write(ref enableCue, null);

    public void MixEnableCue(nint destination, bool foreground, bool layoutSupported)
    {
        var cue = Volatile.Read(ref enableCue);
        if (cue == null) return;
        var current = Volatile.Read(ref settings);
        if (!foreground || !layoutSupported || !current.Enabled || Faulted)
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
        Vector3? Source = null, int SoundId = 0, object? Epoch = null, bool Biased = false);

    public Frame Prepare(GameAudioLayout.SourceVoice* voice, bool owned, byte bus, uint flags, Vector3? position = null, int soundId = 0)
    {
        var current = Volatile.Read(ref settings);
        var combatGain = current.AdditionalGain(owned, bus, flags, false);
        var footstepCandidate = bus == 6 && FootstepsActive;
        if (combatGain <= 0 && !footstepCandidate)
        {
            if (current.Enabled && owned && (flags & 0xE) != 0)
            { Interlocked.Increment(ref preservedNativeBlocks); return new(0, current, 0, Route: HapticRoute.Native); }
            return default;
        }
        var pose = Listener;
        var epoch = Volatile.Read(ref impactEpoch);
        // Resolve on the game's audio thread, using its current graph each time.
        // No heap pointer is retained across an audio-device change or replug.
        var destination = FindVibrationDestination();
        if (destination != 0)
        {
            Volatile.Write(ref available, 1);
            if (GameAudioLayout.TryGetSendGain(voice, destination, out var nativeGain))
            {
                Interlocked.Increment(ref preservedNativeBlocks);
                var extraGain = footstepCandidate ? current.NativeFootstepExtraGain(bus, true, nativeGain) : 0;
                return extraGain > 0 ? new(destination, current, extraGain, Route: HapticRoute.NativeFootstep,
                    SoundId: soundId, Epoch: epoch) : new(0, current, 0, Route: HapticRoute.Native);
            }
            if (combatGain <= 0) return default; // A footstep without a native vibration send stays unmodified.
            if (current.Spatial && current.DistanceFalloff && (!SpatialSupported || !position.HasValue ||
                !HapticSpatializer.ValidPosition(position.Value) || pose == null))
            {
                Interlocked.Increment(ref suppressedBlocks);
                return new(0, current, 0, Route: !SpatialSupported ? HapticRoute.SpatialUnavailable :
                    !position.HasValue || !HapticSpatializer.ValidPosition(position.Value) ? HapticRoute.MissingPosition : HapticRoute.MissingListener);
            }
            var spatial = current.Spatial && SpatialSupported && position.HasValue
                ? HapticSpatializer.Evaluate(current, pose, position.Value, Stopwatch.GetTimestamp()) : default;
            if (current.Spatial && current.DistanceFalloff && !spatial.Valid)
            { Interlocked.Increment(ref suppressedBlocks); return new(0, current, 0, Route: HapticRoute.MissingListener); }
            var biased = false;
            if (spatial.Valid && pose != null && position.HasValue && ImpactCue is { } impact)
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
            return new(destination, current, current.Strength, spatial, spatial.Valid ? HapticRoute.Spatial : HapticRoute.Original,
                position, soundId, epoch, biased);
        }
        Volatile.Write(ref available, 0);
        return default;
    }

    public void Mix(Frame frame, float* scratch, int sampleCount, uint format)
    {
        if (frame.Destination == 0 || scratch == null || sampleCount <= 0 || frame.Gain <= 0 ||
            !ReferenceEquals(frame.Settings, Volatile.Read(ref settings)) ||
            !ReferenceEquals(frame.Epoch, Volatile.Read(ref impactEpoch)) || !Active ||
            frame.Route == HapticRoute.NativeFootstep && !FootstepsActive) return;
        try
        {
            // Validate the complete block before adding it. MixIn reads the same
            // processed samples as native sends; it does not modify this buffer.
            var rms = SfxLoudness.MeasureRms(new ReadOnlySpan<float>(scratch, sampleCount));
            if (rms <= 0) return;
            if (frame.Spatial.Valid)
            {
                var output = stackalloc float[2048];
                var matrix = sampleCount == 4096 ? stereo16 : stereo8;
                if (matrix == null || !HapticSpatializer.Render(new ReadOnlySpan<float>(scratch, sampleCount), matrix,
                    new Span<float>(output, 2048), frame.Spatial)) return;
                ((delegate* unmanaged<nint, float*, uint, float, uint, void>)mixIn)(frame.Destination, output, 256, frame.Gain, 0);
                Interlocked.Increment(ref spatialBlocks);
                if (frame.Biased) Interlocked.Increment(ref biasedBlocks);
                if (frame.Settings?.FollowImpact == true && frame.Source is { } point && Listener is { } pose)
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
                ((delegate* unmanaged<nint, float*, uint, float, uint, void>)mixIn)(frame.Destination, scratch, 256, frame.Gain, format);
                if (frame.Route != HapticRoute.NativeFootstep && frame.Settings?.Spatial == true)
                    Interlocked.Increment(ref unpositionedBlocks);
            }
            if (frame.Route == HapticRoute.NativeFootstep) Interlocked.Increment(ref footstepBlocks);
            else Interlocked.Increment(ref mixedBlocks);
        }
        catch { Interlocked.Exchange(ref faulted, 1); Configure(new HapticSettings(), false, false); }
    }
}
