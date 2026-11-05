// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using DualSenseLighting;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static unsafe class ScreenEffectHapticChecks
{
    private static int calls;
    private static float capturedSumPeak;
    private static readonly float[] mixed = new float[2048];

    [UnmanagedCallersOnly]
    private static void Capture(nint destination, float* pcm, uint frames, float gain, uint format) =>
        CaptureBlock(destination, pcm, gain, false);

    [UnmanagedCallersOnly]
    private static void MixNative(nint destination, float* pcm, uint frames, float gain, uint format) =>
        CaptureBlock(destination, pcm, gain, true);

    private static void CaptureBlock(nint destination, float* pcm, float gain, bool commit)
    {
        calls++; new ReadOnlySpan<float>(pcm, mixed.Length).CopyTo(mixed);
        capturedSumPeak = 0;
        var input = *(float**)(destination + 0x80);
        var hasInput = *(short*)(destination + 0x122) > 0;
        for (var index = 0; index < mixed.Length; index++)
        {
            var sum = (hasInput ? input[index] : 0) + pcm[index] * gain;
            if (index % 8 < 2) capturedSumPeak = Math.Max(capturedSumPeak, Math.Abs(sum));
            if (commit) input[index] = sum;
        }
        (*(short*)(destination + 0x122))++;
    }

    // One native bed and graph are enough to exercise the real mixing boundary.
    private sealed class NativeFixture : IDisposable
    {
        public readonly nint* Table = (nint*)NativeMemory.AllocZeroed(128);
        public readonly nint* Graph = (nint*)NativeMemory.AllocZeroed(120);
        public readonly byte* Destination = (byte*)NativeMemory.AllocZeroed(0x160);
        public readonly byte* Replacement = (byte*)NativeMemory.AllocZeroed(0x160);
        public readonly float* Input = (float*)NativeMemory.AllocZeroed(2048 * sizeof(float));
        public readonly NativeHapticOutput Output;
        public Span<float> Samples => new(Input, 2048);

        public NativeFixture(HapticSettings? requested = null, bool attribution = false, bool commitNative = false)
        {
            Table[15] = commitNative
                ? (nint)(delegate* unmanaged<nint, float*, uint, float, uint, void>)&MixNative
                : (nint)(delegate* unmanaged<nint, float*, uint, float, uint, void>)&Capture;
            Initialize(Destination); Initialize(Replacement); Graph[9] = (nint)Destination;
            Output = CreateOutput();
            Output.Configure(requested ?? new(ScreenEffects: true, ScreenStrength: 0.4f), true, attribution);
        }

        private void Initialize(byte* destination)
        {
            *(nint*)destination = (nint)Table;
            *(int*)(destination + 0xE0) = 1;
            *(nint*)(destination + 0x80) = (nint)Input;
            *(int*)(destination + 0x88) = 256;
            "Vibration\0"u8.CopyTo(new Span<byte>(destination + 0x13E, 10));
        }

        public NativeHapticOutput CreateOutput() => new(Graph, Table[15], (nint)Table, (nint)Table + 128);

        public void SetNative(short count, float left = 0, float right = 0, float other = 0)
        {
            Samples.Clear();
            for (var frame = 0; frame < 256; frame++)
            {
                Input[frame * 8] = left; Input[frame * 8 + 1] = right;
                for (var channel = 2; channel < 8; channel++) Input[frame * 8 + channel] = other;
            }
            *(nint*)(Destination + 0x80) = (nint)Input;
            *(int*)(Destination + 0x88) = 256;
            *(short*)(Destination + 0x122) = count;
        }

        public void Dispose()
        {
            NativeMemory.Free(Input); NativeMemory.Free(Replacement); NativeMemory.Free(Destination);
            NativeMemory.Free(Graph); NativeMemory.Free(Table);
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        var now = Stopwatch.Frequency * 10;
        ScreenEffectSample Measure(uint flags = 0, int mode = 0, float strength = 0, float weight = 1,
            float chromatic = 0, float shake = 0) =>
            ScreenEffectHaptics.Measure(shake, flags, mode, strength, weight,
                chromatic, new(0.2f, 0.4f, 1), now);
        check(Measure().Effects == ScreenEffectKind.None && Measure().Level == 0, "An idle camera generated feedback");
        foreach (var pair in new[] { (0, ScreenEffectKind.RadialBlur), (1, ScreenEffectKind.GaussianBlur), (2, ScreenEffectKind.DirectionalBlur) })
        {
            var sample = Measure(1u << 9, pair.Item1, 0.8f, 0.5f);
            check(sample.Effects == pair.Item2 && Math.Abs(sample.Level - 0.4f) < 0.0001f, "Blur type or applied weight was lost");
        }
        check(Measure(1u << 9, 99, 1).Level == 0 && Measure(0, 0, 1).Level == 0 &&
            Measure(1u << 9, 0, 1, 0).Level == 0, "Unknown, disabled or zero-weight blur generated feedback");
        check(Measure(1u << 18, chromatic: 0.5f).Effects == ScreenEffectKind.ChromaticAberration &&
            Math.Abs(Measure(1u << 18, chromatic: 0.5f).Level - 0.2f) < 0.0001f, "Chromatic aberration ignored its alpha/strength");
        check(Measure(shake: 0.4f) is { Effects: ScreenEffectKind.Shake, Level: 0.4f }, "Observed shake intensity was lost");
        check(Measure(uint.MaxValue & ~((1u << 9) | (1u << 18))).Level == 0,
            "Persistent rendering features such as bloom/DoF generated constant rumble");
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(Measure(1u << 9, 0, value).Level == 0 && Measure(shake: value).Level == 0,
                "Invalid native parameters generated feedback");
        var combined = Measure((1u << 9) | (1u << 18), 0, 0.8f, chromatic: 1, shake: 0.4f);
        check(combined.Level == 0.8f && combined.DominantEffect == ScreenEffectKind.RadialBlur &&
            combined.Effects.HasFlag(ScreenEffectKind.Shake | ScreenEffectKind.RadialBlur | ScreenEffectKind.ChromaticAberration),
            "Simultaneous screen effects stacked their strength or lost their identity");
        check(ScreenEffectHaptics.Level(combined, now) == 0.8f &&
            ScreenEffectHaptics.Level(combined, now + ScreenEffectHaptics.FreshTicks + 1) == 0 &&
            ScreenEffectHaptics.Level(combined, now - 1) == 0, "Stale or future screen samples played");
        check(ScreenEffectHaptics.Level(combined with { ExpiresAt = now + 1 }, now + 1) == 0,
            "A missing camera frame sustained its previous jolt beyond its live deadline");
        check(new Configuration() is { HapticScreenEffects: true, HapticScreenStrength: 0.25f } && !new HapticSettings().ScreenEffects,
            "User defaults or inactive internal screen-output settings changed");
        check(new HapticSettings(ScreenEffects: true).NeedsAudio &&
            new HapticSettings(ScreenStrength: float.NaN).Normalize().ScreenStrength == 0.25f &&
            new HapticSettings(ScreenStrength: 2).Normalize().ScreenStrength == 1, "Screen settings were not bounded");
        CheckCurrentBlockMix(check);
        CheckHeadroom(check);
        CheckGuardsAndLifecycle(check);
        CheckSfxAndConfirmation(check);
        CheckFightReplay(check);
        CheckNonSpatialOutput(check);
        CheckTextureAndStrength(check);
        CameraShakeChecks.Run(check);
    }

    private static void CheckCurrentBlockMix(Action<bool, string> check)
    {
        using var fixture = new NativeFixture();
        var output = fixture.Output; var destination = (nint)fixture.Destination;
        var now = Stopwatch.GetTimestamp();
        var sample = new ScreenEffectSample(ScreenEffectKind.Shake, 1, now);
        output.SetScreenEffects(sample, output.ScreenSettings);
        foreach (short count in new short[] { 1, 2, 8, 50 })
        {
            fixture.SetNative(count, 0.02f, -0.032f, 0.2f);
            var original = fixture.Samples.ToArray(); var before = calls;
            output.MixScreenEffects(destination, true, true, now);
            check(calls == before + 1 && output.LastScreenMix is { State: "mixed", Mixed: true, InputInvalid: false } &&
                output.LastScreenMix.InputCount == count && output.LastScreenMix.Clock == now &&
                output.LastScreenMix.SourceTimestamp == sample.Timestamp && output.LastScreenMix.EffectiveLevel == 0.4f &&
                output.LastScreenMix.OutputPeak > 0 && output.LastScreenMix.GripPeak == 0.032f && output.LastScreenMix.OtherPeak == 0.2f,
                "A current camera effect waited for another native input or lost its source diagnostic");
            check(original.AsSpan().SequenceEqual(fixture.Samples) && capturedSumPeak <= 1,
                "Screen feedback altered native PCM or exceeded combined grip output");
        }
        fixture.SetNative(1, 1, -1);
        var callsBeforeBusy = calls;
        output.MixScreenEffects(destination, true, true, now + 1);
        check(calls == callsBeforeBusy && output.LastScreenMix is { State: "headroom", Mixed: false, OutputPeak: 0, Headroom: 0 },
            "A full native block reported a successful silent screen mix");
        fixture.SetNative(0, 1, -1);
        output.MixScreenEffects(destination, true, true, now + 2);
        check(calls == callsBeforeBusy + 1 && output.LastScreenMix is { State: "mixed", Headroom: 1, LimitedFrames: 0 },
            "An earlier busy block delayed the next camera response or a zero-count block read stale PCM");
        fixture.SetNative(1, 0.02f, -0.02f);
        output.MixScreenEffects(destination, true, true, now); // Warm the measured callback path.
        var allocationBlock = 0;
        Action mixBlock = () =>
        {
            var i = allocationBlock++;
            fixture.SetNative((short)(1 + i % 8), 0.02f, -0.02f);
            output.MixScreenEffects(destination, true, true, now + i);
        };
        var allocated = AllocationChecks.MeasureAllocations(mixBlock, warmup: 0, iterations: 100);
        check(allocated == 0, $"Screen mixing or its diagnostics allocated {allocated} bytes on the audio thread");
    }

    private static void CheckHeadroom(Action<bool, string> check)
    {
        var renderer = new ScreenEffectHaptics();
        var pcm = new float[2048]; var native = new float[2048];
        for (var frame = 0; frame < 256; frame++)
        {
            native[frame * 8] = frame % 2 == 0 ? 0.9f : -0.8f;
            native[frame * 8 + 1] = frame % 2 == 0 ? -0.2f : 0.95f;
            for (var channel = 2; channel < 8; channel++) native[frame * 8 + channel] = 1;
        }
        var original = native.ToArray();
        check(renderer.Render(pcm, 1, ScreenEffectKind.Shake, native) && renderer.LimitedFrames > 0 &&
            Math.Abs(renderer.MinimumHeadroom - 0.05f) < 0.000001f && renderer.OutputPeak > 0,
            "The current native block did not constrain generated PCM per frame");
        check(original.AsSpan().SequenceEqual(native), "Headroom limiting modified native input");
        for (var frame = 0; frame < 256; frame++)
        {
            check(pcm[frame * 8] == pcm[frame * 8 + 1] &&
                Math.Abs(native[frame * 8] + pcm[frame * 8]) <= 1 &&
                Math.Abs(native[frame * 8 + 1] + pcm[frame * 8 + 1]) <= 1,
                "Common headroom panned the generated shake or clipped a grip");
            for (var channel = 2; channel < 8; channel++)
                check(pcm[frame * 8 + channel] == 0, "Screen feedback wrote another bed channel");
        }
        Array.Clear(native); native[128 * 8] = 1;
        check(renderer.Render(pcm, 1, ScreenEffectKind.Shake, native) && renderer.MinimumHeadroom == 0 &&
            renderer.LimitedFrames == 1 && pcm[128 * 8] == 0 && pcm[128 * 8 + 1] == 0 && renderer.OutputPeak > 0.5f,
            "One full-scale native sample muted the whole screen block");
        for (var frame = 0; frame < 256; frame++)
        {
            native[frame * 8] = native[frame * 8 + 1] = 0;
            for (var channel = 2; channel < 8; channel++) native[frame * 8 + channel] = 1;
        }
        check(renderer.Render(pcm, 0.4f, ScreenEffectKind.Shake, native) && renderer.MinimumHeadroom == 1 &&
            renderer.LimitedFrames == 0 && renderer.OutputPeak > 0.3f,
            "A non-grip bed channel reduced the camera response");
        check(!renderer.Render(pcm, 0) && pcm.All(x => x == 0) && renderer.OutputPeak == 0,
            "A real camera pause left generated PCM or a positive output diagnostic");
    }

    private static void CheckGuardsAndLifecycle(Action<bool, string> check)
    {
        using var fixture = new NativeFixture();
        var output = fixture.Output; var destination = (nint)fixture.Destination;
        var now = Stopwatch.GetTimestamp(); var sample = new ScreenEffectSample(ScreenEffectKind.Shake, 1, now);
        output.SetScreenEffects(sample, output.ScreenSettings);
        void Guard(string state, bool foreground = true, bool supported = true, nint? target = null)
        {
            var before = calls;
            output.MixScreenEffects(target ?? destination, foreground, supported, now);
            check(calls == before && !output.LastScreenMix.Mixed && output.LastScreenMix.State == state,
                "Screen output bypassed its " + state + " guard");
        }
        Guard("background", foreground: false);
        Guard("unsupported-audio", supported: false);
        Guard("invalid-destination", target: 0);
        Guard("invalid-destination", target: (nint)fixture.Replacement);
        fixture.SetNative(-1); Guard("invalid-native-input");
        check(output.LastScreenMix.InputCount == -1, "A negative input count was not diagnosed");
        foreach (var capacity in new[] { -1, 0, 255 })
        {
            fixture.SetNative(1); *(int*)(fixture.Destination + 0x88) = capacity;
            Guard("invalid-native-input");
        }
        fixture.SetNative(1); *(nint*)(fixture.Destination + 0x80) = 0; Guard("invalid-native-input");
        foreach (var index in new[] { 0, 7 })
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            fixture.SetNative(1); fixture.Input[index] = invalid; Guard("invalid-native-input");
            check(output.LastScreenMix.InputInvalid && float.IsFinite(output.LastScreenMix.GripPeak) &&
                float.IsFinite(output.LastScreenMix.OtherPeak), "Invalid native PCM produced unserializable diagnostics");
        }
        fixture.SetNative(0); *(nint*)(fixture.Destination + 0x80) = 1;
        var before = calls; output.MixScreenEffects(destination, true, true, now);
        check(calls == before + 1 && output.LastScreenMix.Mixed, "A zero-count native block dereferenced a stale buffer");
        fixture.SetNative(0);
        output.SetScreenEffects(sample with { Timestamp = now - ScreenEffectHaptics.FreshTicks - 1 }, output.ScreenSettings); Guard("stale");
        output.SetScreenEffects(sample with { ExpiresAt = now }, output.ScreenSettings); Guard("expired");
        output.SetScreenEffects(sample with { Timestamp = now + 1 }, output.ScreenSettings); Guard("future");
        output.SetScreenEffects(null, output.ScreenSettings); Guard("quiet");
        check(output.LastScreenMix.SourceTimestamp == 0, "Quiet output retained an earlier source timestamp");
        var previous = output.ScreenSettings;
        output.Configure(new(ScreenEffects: true, ScreenStrength: 0.2f), true, false);
        output.SetScreenEffects(sample, previous); Guard("quiet");
        check(output.ScreenSample == null, "A stale settings snapshot published across a strength change");
        output.SetScreenEffects(sample, output.ScreenSettings);
        fixture.Graph[9] = (nint)fixture.Replacement; Guard("invalid-destination");
        before = calls; output.MixScreenEffects((nint)fixture.Replacement, true, true, now);
        check(calls == before + 1 && output.LastScreenMix.Mixed, "A replacement vibration graph did not respond immediately");
        output.Configure(new(ScreenEffects: true), false, false);
        Guard("disabled");
        check(output.ScreenSample == null && !output.Active, "Background configuration retained active screen feedback");
    }

    private static void CheckSfxAndConfirmation(Action<bool, string> check)
    {
        using var fixture = new NativeFixture(new(Enabled: true, Strength: 0.1f, Spatial: false, DistanceFalloff: false,
            ScreenEffects: true, ScreenStrength: 0.7f), attribution: true, commitNative: true);
        var output = fixture.Output; var destination = (nint)fixture.Destination;
        Span<float> source = stackalloc float[2048]; source.Fill(0.2f);
        var frame = output.Prepare(null, true, 2, 0);
        var before = calls;
        fixed (float* pcm = source) output.Mix(frame, pcm, source.Length, 0);
        check(calls == before + 1 && fixture.Input[0] > 0, "The SFX coexistence fixture produced no native input");
        var now = Stopwatch.GetTimestamp();
        output.SetScreenEffects(new(ScreenEffectKind.Shake, 1, now), output.ScreenSettings);
        before = calls; output.MixScreenEffects(destination, true, true, now);
        check(calls == before + 1 && output.LastScreenMix.Mixed && output.LastScreenMix.GripPeak > 0,
            "A skill's SFX prevented the current camera effect from mixing beside it");
        fixture.SetNative(0); output.RequestEnableCue();
        check(output.HasEnableCue && output.ScreenEffectLevel(now) > 0, "An enable cue removed the camera's current level");
        before = calls; output.MixEnableCue(destination, true, true);
        check(calls == before + 1 && fixture.Samples.ToArray().Any(x => x != 0), "The cue coexistence fixture produced no native input");
        now = Stopwatch.GetTimestamp();
        output.SetScreenEffects(new(ScreenEffectKind.Shake, 1, now), output.ScreenSettings);
        before = calls; output.MixScreenEffects(destination, true, true, now);
        check(calls == before + 1 && output.LastScreenMix.Mixed && output.LastScreenMix.GripPeak > 0 && capturedSumPeak <= 1,
            "An enable confirmation prevented camera feedback or clipped their combined output");
    }

    private static void CheckFightReplay(Action<bool, string> check)
    {
        using var fixture = new NativeFixture(new(ScreenEffects: true, ScreenStrength: 1));
        var output = fixture.Output; var destination = (nint)fixture.Destination;
        var start = Stopwatch.GetTimestamp(); var initialCalls = calls;
        for (var frame = 0; frame < 194; frame++)
        {
            var now = start + frame * Stopwatch.Frequency / 97;
            var level = 0.18f + 0.65f * (0.5f - 0.5f * MathF.Cos(2 * MathF.PI * frame / 97));
            fixture.SetNative((short)(1 + frame % 8), 0.032f * (frame % 9) / 8, -0.016f);
            output.SetScreenEffects(new(ScreenEffectKind.Shake, level, now), output.ScreenSettings);
            var before = calls; output.MixScreenEffects(destination, true, true, now);
            check(calls == before + 1 && output.LastScreenMix.Mixed && output.LastScreenMix.SourceTimestamp == now &&
                output.LastScreenMix.OutputPeak > 0 && output.LastScreenMix.LimitedFrames == 0 &&
                Math.Abs(output.LastScreenMix.EffectiveLevel - MathF.Sqrt(level)) < 0.00001f,
                "A 97 FPS camera frame was delayed, lost, or replaced by an earlier haptic level");
        }
        check(calls == initialCalls + 194, "The two-second fight replay missed an active camera frame");
        var pause = start + Stopwatch.Frequency * 2;
        output.SetScreenEffects(new(ScreenEffectKind.None, 0, pause), output.ScreenSettings);
        var atPause = calls; output.MixScreenEffects(destination, true, true, pause);
        check(calls == atPause && output.ScreenEffectLevel(pause) == 0 && output.LastScreenMix.State == "quiet",
            "A real camera pause kept the last shake vibrating");
        var resumed = pause + Stopwatch.Frequency / 97;
        fixture.SetNative(8, 0.032f, -0.032f);
        output.SetScreenEffects(new(ScreenEffectKind.Shake, 0.18f, resumed), output.ScreenSettings);
        output.MixScreenEffects(destination, true, true, resumed);
        check(calls == atPause + 1 && output.LastScreenMix.Mixed, "A new shake waited after a real camera pause");
    }

    private static void CheckNonSpatialOutput(Action<bool, string> check)
    {
        using var fixture = new NativeFixture();
        var destination = fixture.Destination;
        var now = Stopwatch.Frequency * 10;
        var ready = now + Stopwatch.Frequency / 20;
        var requested = new HapticSettings(ScreenEffects: true, ScreenStrength: 0.4f);
        var sample = new ScreenEffectSample(ScreenEffectKind.RadialBlur, 0.75f, now);
        var cases = new[]
        {
            requested,
            requested with { Spatial = true, DistanceFalloff = true, AutomaticDistance = false, NearDistance = 0, FarDistance = 1 },
            requested with { Spatial = true, DistanceFalloff = true, DistanceOrigin = HapticDistanceOrigin.Camera,
                AutomaticDistance = false, NearDistance = 1, FarDistance = 2, FollowImpact = true, ImpactBias = 1 },
            requested with { Spatial = false, DistanceFalloff = false, Strength = 0, EnemyStrength = 0 },
        };
        HapticListenerPose?[] poses =
        [
            null,
            new(Vector3.Zero, Vector3.UnitX, ready),
            new(new(1000, 0, 1000), -Vector3.UnitX, ready, new(5000, 2000, 5000)),
            new(new(-1000, 200, -1000), Vector3.UnitZ, ready, new(-5000, 3000, -5000)),
        ];
        float[]? baseline = null;
        foreach (var settings in cases)
        foreach (var pose in poses)
        {
            var output = fixture.CreateOutput();
            output.Configure(settings, true, false);
            output.SetListener(pose); // No position, stereo matrix or spatial support is required.
            output.SetScreenEffects(sample, output.ScreenSettings);
            var before = calls;
            output.MixScreenEffects((nint)destination, true, true, ready);
            check(calls == before + 1 && Math.Abs(output.ScreenEffectLevel(ready) - MathF.Sqrt(0.75f) * 0.4f) < 0.0001f,
                "Screen vibration faded with character/camera distance, direction or SFX strength");
            baseline ??= mixed.ToArray();
            check(baseline.AsSpan().SequenceEqual(mixed), "Spatial settings or camera pose changed screen PCM");
            for (var frame = 0; frame < 256; frame++)
                check(mixed[frame * 8] == mixed[frame * 8 + 1], "Screen effects panned to one grip");
        }

        foreach (var rumble in new[] { false, true })
        {
            var output = fixture.CreateOutput();
            output.Configure(requested, true, false, useRumble: rumble);
            var screenSettings = output.ScreenSettings;
            output.SetScreenEffects(sample, screenSettings);
            var level = output.ScreenEffectLevel(ready);
            foreach (var settings in cases)
            {
                output.Configure(settings, true, false, useRumble: rumble);
                check(ReferenceEquals(screenSettings, output.ScreenSettings) && output.ScreenEffectLevel(ready) == level &&
                    ReferenceEquals(output.ScreenSample, sample),
                    "Changing spatial/SFX settings interrupted or restarted an active screen effect");
            }
            output.Configure(requested with { ScreenStrength = 0.2f }, true, false, useRumble: rumble);
            check(output.ScreenSample == null && output.ScreenEffectLevel(ready) == 0,
                "Screen output retained a sample across its own strength change");
            output.SetScreenEffects(sample, screenSettings);
            check(output.ScreenSample == null, "A stale screen-settings snapshot published after a strength change");
            output.SetScreenEffects(sample, output.ScreenSettings);
            output.Configure(requested with { RumbleController = 2 }, true, false, useRumble: rumble);
            check(output.ScreenSample == null, "Screen output crossed a controller selection change");
            output.SetScreenEffects(sample, output.ScreenSettings);
            output.Configure(requested with { ScreenEffects = false }, true, false, useRumble: rumble);
            check(output.ScreenSample == null && !output.ScreenEffectsActive, "Screen output survived its own disable");
        }
    }

    private static void CheckTextureAndStrength(Action<bool, string> check)
    {
        check(ScreenEffectHaptics.Response(0, 0.25f) == 0 && ScreenEffectHaptics.Response(1, 0.25f) == 0.25f &&
            ScreenEffectHaptics.Response(0.04f, 0.25f) == 0.05f && ScreenEffectHaptics.Response(1, 0) == 0 &&
            ScreenEffectHaptics.Response(float.NaN, 1) == 0 && ScreenEffectHaptics.Response(1, float.NaN) == 0,
            "Screen response must lift weak effects, preserve silence and respect the user's strength limit");
        const int frames = 9600;
        var pcm = new float[2048];
        float[] Render(ScreenEffectKind effect, float gain)
        {
            var renderer = new ScreenEffectHaptics();
            var mono = new float[frames];
            for (var position = 0; position < frames; position += 256)
            {
                check(renderer.Render(pcm, gain, effect), "An active effect produced no texture");
                for (var frame = 0; frame < Math.Min(256, frames - position); frame++)
                {
                    mono[position + frame] = pcm[frame * 8];
                    check(pcm[frame * 8] == pcm[frame * 8 + 1] && float.IsFinite(pcm[frame * 8]) &&
                        Math.Abs(pcm[frame * 8]) <= gain, "Texture panned or exceeded the selected strength");
                }
            }
            return mono;
        }
        var shake = Render(ScreenEffectKind.Shake, 0.25f);
        var radial = Render(ScreenEffectKind.RadialBlur, 0.25f);
        check(SfxLoudness.MeasureRms(shake) > 0.09f, "Camera shake retained the old extra 75% amplitude reduction");
        check(!shake.AsSpan().SequenceEqual(radial), "Camera shake and radial blur still feel like the same generated tone");
        float Energy(float[] signal, float frequency)
        {
            double real = 0, imaginary = 0;
            for (var i = 0; i < signal.Length; i++)
            {
                var phase = 2 * Math.PI * frequency * i / 48000;
                real += signal[i] * Math.Cos(phase);
                imaginary += signal[i] * Math.Sin(phase);
            }
            return (float)(real * real + imaginary * imaginary);
        }
        check(Energy(shake, 60) > Energy(shake, 50) * 100 && Energy(shake, 60) > Energy(shake, 95) * 100 &&
            Energy(radial, 65) > Energy(radial, 60) * 10,
            "Shake regained an independent beating/noise texture or blur lost its separate smooth texture");
        var profiles = new[] { ScreenEffectKind.DirectionalBlur, ScreenEffectKind.GaussianBlur, ScreenEffectKind.ChromaticAberration };
        foreach (var profile in profiles)
        {
            var signal = Render(profile, 0.25f);
            check(SfxLoudness.MeasureRms(signal) > 0.09f && !signal.AsSpan().SequenceEqual(shake) &&
                !signal.AsSpan().SequenceEqual(radial), "An effect reused the shake/blur texture or became too weak");
        }
        var changing = new ScreenEffectHaptics();
        changing.Render(pcm, 0.25f, ScreenEffectKind.Shake);
        changing.Render(pcm, 0.05f, ScreenEffectKind.Shake);
        check(pcm.All(value => Math.Abs(value) <= 0.05f), "Shake did not follow a weaker current camera effect");
        check(!changing.Render(pcm, 0, ScreenEffectKind.Shake) && pcm.All(value => value == 0),
            "A finished shake continued vibrating");
        var allocationFrame = 0;
        Action renderTexture = () => changing.Render(pcm, 0.25f, profiles[allocationFrame++ % profiles.Length]);
        var allocated = AllocationChecks.MeasureAllocations(renderTexture, warmup: 0, iterations: 100);
        check(allocated == 0, $"Changing screen textures allocated {allocated} bytes on the audio thread");
    }

}
