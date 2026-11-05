// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static class CameraShakeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckCurrentFrameTiming(check);
        var origin = Stopwatch.Frequency * 10;
        long At(double seconds) => origin + (long)Math.Round(seconds * Stopwatch.Frequency);
        var tracker = new CameraShakeTracker();
        check(tracker.Observe(new(0.1f, 0, 0), Vector3.Zero, At(0)) > 0,
            "An initial visible shake waited for a historical motion baseline");
        check(tracker.Observe(new(0.1f, 0, 0), Vector3.Zero, At(1 / 60.0)) > 0 && tracker.UnchangedFrames == 1,
            "A repeated nonzero camera frame cut a held shake extremum");
        for (var frame = 2; frame <= 20; frame++)
        {
            var level = tracker.Observe(new(0.1f, 0, 0), Vector3.Zero, At(frame / 60.0));
            if (frame >= 3) check(level == 0, "A stationary camera effect sustained the old repeating rumble");
        }
        check(Math.Abs(tracker.RawMagnitude - 0.4f) < 0.00001f && tracker.LastChangeTimestamp == At(0),
            "Static-settling diagnostics lost the actual offset or its last change");
        CheckLightShake(check, origin);

        float[] Wave(float amplitude, float frequency, int fps, Vector3 axis, bool rotation = false)
        {
            var motion = new CameraShakeTracker();
            var values = new float[fps * 3 + 1];
            for (var i = 0; i < values.Length; i++)
            {
                var offset = axis * (amplitude * MathF.Sin(2 * MathF.PI * frequency * i / fps));
                values[i] = motion.Observe(rotation ? Vector3.Zero : offset, rotation ? offset : Vector3.Zero, At(i / (double)fps));
                check(float.IsFinite(values[i]) && values[i] is >= 0 and <= 1, "Camera motion produced an invalid haptic level");
            }
            return values;
        }
        var slow = Wave(0.1f, 2, 120, Vector3.UnitX);
        var fast = Wave(0.1f, 5, 120, Vector3.UnitX);
        var weaker = Wave(0.025f, 2, 120, Vector3.UnitX);
        var vertical = Wave(0.1f, 2, 120, Vector3.UnitY);
        var reverse = Wave(0.1f, 2, 120, -Vector3.UnitX);
        var rotational = Wave(0.1f, 2, 120, Vector3.UnitZ, true);
        double Average(float[] values)
        {
            double total = 0;
            for (var i = 10; i < values.Length; i++) total += values[i];
            return total / (values.Length - 10);
        }
        check(Average(slow) > Average(weaker) * 1.1 && Math.Abs(Average(fast) / Average(slow) - 1) < 0.08,
            "Shake lost graded amplitude or changed strength merely because its rhythm was faster");
        check(vertical.AsSpan().SequenceEqual(slow) && reverse.AsSpan().SequenceEqual(slow),
            "Shake motion was attenuated by axis or direction");
        check(Average(rotational) > 0, "Rotational camera shake was missed");
        int Peaks(float[] values)
        {
            var peaks = 0;
            for (var i = 12; i < values.Length - 1; i++)
                if (values[i] > values[i - 1] && values[i] >= values[i + 1] && values[i] > 0.01f) peaks++;
            return peaks;
        }
        check(Peaks(fast) > Peaks(slow) * 2, "Faster camera shake retained an unrelated fixed vibration rhythm");
        // Compare the current visible frame, not the previous frame accepted by
        // the old acceleration estimator. Both strokes remain equally strong.
        var aligned = 0;
        for (var i = 12; i < slow.Length; i++)
        {
            var actual = Math.Abs(Math.Sin(2 * Math.PI * 2 * i / 120.0));
            if (actual < 0.0001) { check(slow[i] == 0, "A current zero-offset frame retained a vibration beat"); aligned++; }
            if (actual > 0.99) { check(slow[i] > 0.39f, "A visible shake extremum missed its current-frame haptic peak"); aligned++; }
        }
        check(aligned > 20, "Camera rhythm fixture did not exercise enough peaks and pauses");
        foreach (var fps in new[] { 30, 60, 120 })
        {
            var values = Wave(0.1f, 2, fps, Vector3.UnitX);
            check(Math.Abs(Average(values) / Average(slow) - 1) < 0.12,
                "Frame rate changed the strength of the same camera shake");
        }

        // The same current offsets must produce the same drive regardless of the
        // spacing of earlier samples. Historical timing may only bound static holds.
        tracker.Reset();
        var times = new[] { 0.0, 0.01, 0.03, 0.045, 0.07, 0.085 };
        for (var i = 0; i < times.Length; i++)
        {
            var t = times[i];
            var offset = new Vector3((float)(2 * t * t), 0, 0);
            var level = tracker.Observe(offset, Vector3.Zero, At(t));
            var fresh = new CameraShakeTracker().Observe(offset, Vector3.Zero, At(t));
            check(level == fresh, "Uneven frame times distorted the current applied shake magnitude");
        }
        var stoppedAt = At(0.1);
        check(tracker.Observe(Vector3.Zero, Vector3.Zero, stoppedAt) == 0,
            "Finished camera motion waited for historical derivatives before stopping");
        check(tracker.Observe(new(1, 0, 0), Vector3.Zero, At(1)) ==
            new CameraShakeTracker().Observe(new(1, 0, 0), Vector3.Zero, At(1)),
            "A stale gap carried historical camera motion into a fresh frame");
        check(tracker.Observe(new(float.NaN, 0, 0), Vector3.Zero, At(1.01)) == 0 && tracker.RawMagnitude == 0,
            "An invalid camera read generated feedback or retained a stale diagnostic magnitude");
        tracker.Reset();
        check(tracker.IntervalTicks == 0 && tracker.RawMagnitude == 0 && tracker.UnchangedFrames == 0 && tracker.LastChangeTimestamp == 0,
            "A new territory/player inherited timing or motion diagnostics from the old camera");
        check(tracker.Observe(Vector3.Zero, Vector3.Zero, At(2)) == 0 &&
            tracker.Observe(new(0.1f, 0, 0), Vector3.Zero, At(1.99)) == 0 &&
            tracker.Observe(Vector3.Zero, Vector3.Zero, At(2.01)) == 0,
            "A backward camera timestamp created feedback");
        var allocationFrame = 1;
        Action observeFrame = () => tracker.Observe(Vector3.Zero, Vector3.Zero, At(2 + allocationFrame++ / 120.0));
        var allocated = AllocationChecks.MeasureAllocations(observeFrame, warmup: 0, iterations: 100);
        check(allocated == 0, $"Camera tracking allocated {allocated} bytes across game frames");

        CheckOutputRhythm(check, origin);
    }

    private static void CheckLightShake(Action<bool, string> check, long origin)
    {
        var tracker = new CameraShakeTracker();
        var interval = Stopwatch.Frequency / 60;
        check(tracker.Observe(new(0.00000001f, 0, 0), Vector3.Zero, origin) == 0,
            "Numerical camera noise triggered the minimum shake drive");
        var light = tracker.Observe(new(0.0001f, 0, 0), Vector3.Zero, origin + interval);
        var strength = 0.25f;
        var level = ScreenEffectHaptics.Response(light, strength);
        check(level >= strength * 0.4f && level <= strength,
            "A real light shake remained imperceptibly weak or exceeded the selected strength");
        check(ScreenEffectHaptics.Response(light, 0) == 0 && tracker.Observe(Vector3.Zero, Vector3.Zero, origin + interval * 2) == 0,
            "The minimum light-shake response bypassed zero strength or a zero camera offset");
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 101f })
        {
            check(tracker.Observe(new(invalid, 0, 0), Vector3.Zero, origin + interval * 3) == 0,
                "An invalid translation generated a light-shake floor");
            check(tracker.Observe(Vector3.Zero, new(0, invalid, 0), origin + interval * 4) == 0,
                "An invalid rotation generated a light-shake floor");
        }
        // A slow effect track may hold its value across two faster camera ticks.
        tracker.Reset();
        var first = tracker.Observe(new(0.1f, 0, 0), Vector3.Zero, origin);
        check(tracker.Observe(new(0.1f, 0, 0), Vector3.Zero, origin + Stopwatch.Frequency / 100) == first &&
            tracker.Observe(new(0.1f, 0, 0), Vector3.Zero, origin + Stopwatch.Frequency / 50) == first,
            "Held effect-track frames incorrectly cut a visible shake extremum");
        check(tracker.Observe(new(0.1f, 0, 0), Vector3.Zero, origin + Stopwatch.Frequency / 25) == 0,
            "Static nonzero offsets did not settle after the bounded hold");
        check(tracker.Observe(new(-0.1f, 0, 0), Vector3.Zero, origin + Stopwatch.Frequency / 20) == first &&
            tracker.UnchangedFrames == 0 && tracker.LastChangeTimestamp == origin + Stopwatch.Frequency / 20,
            "A new stroke did not resume immediately at equal strength");
    }

    private static void CheckCurrentFrameTiming(Action<bool, string> check)
    {
        var tracker = new CameraShakeTracker();
        var start = Stopwatch.Frequency * 10;
        var levels = new float[6];
        for (var frame = 0; frame < levels.Length; frame++)
            levels[frame] = tracker.Observe(frame == 2 ? new Vector3(0.1f, 0, 0) : Vector3.Zero,
                Vector3.Zero, start + frame * Stopwatch.Frequency / 60);
        Console.WriteLine("Camera impulse levels: " + string.Join(", ", Array.ConvertAll(levels, level => level.ToString("F3"))));
        check(levels[2] > 0 && levels[3] == 0 && levels[4] == 0,
            "Camera shake peaked after its visible frame or produced a delayed vibration tail");
    }

    private static unsafe void CheckOutputRhythm(Action<bool, string> check, long origin)
    {
        var tracker = new CameraShakeTracker();
        var output = new NativeHapticOutput(null, 0, 0, 0);
        output.Configure(new HapticSettings(ScreenEffects: true, ScreenStrength: 0.4f), true, false, useRumble: true);
        var renderer = new ScreenEffectHaptics();
        Span<float> pcm = stackalloc float[2048];
        var previousPeak = 0f;
        for (var i = 0; i <= 120; i++)
        {
            var now = origin + (long)Math.Round(i * Stopwatch.Frequency / 120.0);
            // Two separate shake bursts, with a quiet interval between them.
            var offset = i is >= 12 and <= 36 or >= 72 and <= 96
                ? new Vector3(0.1f * MathF.Sin(2 * MathF.PI * 5 * i / 120), 0, 0) : Vector3.Zero;
            var motion = tracker.Observe(offset, Vector3.Zero, now);
            var sample = ScreenEffectHaptics.Measure(motion, 0, 0, 0, 0, 0, Vector3.Zero, now);
            output.SetScreenEffects(sample, output.ScreenSettings);
            var level = output.ScreenEffectLevel(now);
            renderer.Render(pcm, level, sample.Texture);
            var peak = 0f;
            foreach (var value in pcm) peak = Math.Max(peak, Math.Abs(value));
            check(peak <= 0.4f, "Motion-driven output exceeded the screen strength slider");
            if (i is >= 40 and <= 69 or >= 100)
                check(level == 0 && peak == 0, "The output filled real pauses with a generated vibration pattern");
            if (i == 18) previousPeak = peak;
            if (i == 78) check(peak > 0 && previousPeak > 0, "A later camera shake burst was lost");
        }
    }
}
