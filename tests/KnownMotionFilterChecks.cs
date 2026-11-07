// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using DualSenseLighting.Core;

internal static class KnownMotionFilterChecks
{
    private static SecondaryVectorSample Sample(float y, long source = 1, int slot = 0) =>
        new(source, source + 100, source + 200, slot, true, 0, y, 0);

    // Inject a small independently specified bank through the same constructor
    // used by the real immutable bank. No production test-access API is needed.
    private static KnownMotionFilter Fixture(bool position = false, bool loop = true, double seconds = 2,
        float[]? y = null, float[]? x = null)
    {
        var type = typeof(KnownMotionFilter).GetNestedType("Pattern", BindingFlags.NonPublic)!;
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(bool), typeof(double), typeof(bool), typeof(float[]), typeof(float[]) }, null)!;
        var profile = constructor.Invoke(new object?[] { position, seconds, loop, x, y ?? new[] { 0f, 1f, 0f, -1f, 0f } });
        var bank = Array.CreateInstance(type, 1); bank.SetValue(profile, 0);
        return (KnownMotionFilter)typeof(KnownMotionFilter).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(long), bank.GetType() }, null)!.Invoke(new object[] { 1000L, bank });
    }
    private static KnownMotionFilter Match(bool position = false)
    {
        var filter = Fixture(position);
        filter.Observe(Sample(.5f), position, 0);
        filter.Observe(Sample(.75f), position, 125);
        filter.Observe(Sample(1), position, 250);
        _ = filter.GetGain(position ? new(0, 1, 0) : default, position ? default : new(0, 1, 0), 250);
        return filter;
    }
    internal static void Run(Action<bool, string> check)
    {
        CheckSequence(check);
        CheckNativeRealign(check);
        CheckSmallPhaseJitter(check);
        CheckFalseControls(check);
        CheckAccounting(check);
        CheckUncertainty(check);
        CheckNonloopAndImmutableBank(check);
        CheckAllocations(check);
    }

    private static void CheckSequence(Action<bool, string> check)
    {
        var filter = Fixture();
        filter.Observe(Sample(.5f), false, 0);
        check(filter.GetGain(default, new(0, .5f, 0), 0) == 0, "The first plausible vehicle sample leaked a startup vibration");
        filter.Observe(Sample(.75f), false, 125);
        check(filter.GetGain(default, new(0, .75f, 0), 125) == 0, "A continuing startup candidate leaked vibration before confirmation");
        filter.Observe(Sample(1), false, 250);
        check(filter.GetGain(default, new(0, 1, 0), 250) == 0, "Three timestamp-consistent known-profile points were not suppressed");
        filter.Observe(Sample(.2f), false, 251);
        check(filter.GetGain(default, new(0, .2f, 0), 251) == 1, "A changed point retained suppression until a later timer");
        filter = Fixture();
        // Initial .5 has both rising and falling possible phases; retain the falling branch.
        filter.Observe(Sample(.5f), false, 0); filter.Observe(Sample(.25f), false, 125); filter.Observe(Sample(0), false, 250);
        filter.Observe(Sample(-.25f), false, 375);
        check(filter.GetGain(default, new(0, -.25f, 0), 375) == 0, "Ambiguous Y-only first sample discarded the legitimate phase branch");
        filter = Fixture();
        filter.Observe(Sample(.5f), false, 0); filter.Observe(Sample(.5f), false, 0); filter.Observe(Sample(.5f), false, 0);
        check(filter.GetGain(default, new(0, .5f, 0), 0) == 0, "A duplicate startup observation leaked its provisional vehicle candidate");
        filter.Observe(Sample(1), false, 1);
        check(filter.GetGain(default, new(0, 1, 0), 1) == 1,
            "Duplicate timestamps confirmed a trajectory or a mismatch kept provisional suppression");
        filter = Fixture(); filter.Observe(Sample(.5f), false, 0);
        _ = filter.GetGain(default, new(0, .5f, 0), 0);
        filter.Observe(Sample(.9f), false, 10);
        check(filter.GetGain(default, new(0, .9f, 0), 10) == 1,
            "Startup mismatch failed to restore full output on that same update");
        filter.Observe(Sample(.1f), false, 20);
        check(filter.GetGain(default, new(0, .1f, 0), 20) == 1,
            "Repeated reseeding hid unrelated motion by rearming startup suppression");
    }

    private static void CheckFalseControls(Action<bool, string> check)
    {
        var filter = Fixture();
        foreach (var pair in new[] { (0L, .5f), (125L, 0f), (250L, -.5f), (375L, -1f) })
        {
            filter.Observe(Sample(pair.Item2), false, pair.Item1);
            check(filter.GetGain(default, new(0, pair.Item2, 0), pair.Item1) == (pair.Item1 == 0 ? 0 : 1),
                "A different-frequency sequence remained suppressed beyond its initial plausible prefix");
        }
        filter = Fixture();
        for (var i = 0; i < 4; i++)
        {
            filter.Observe(Sample(1.25f), false, i * 30);
            check(filter.GetGain(default, new(0, 1.25f, 0), i * 30) == 1,
                "Out-of-profile absolute amplitude was suppressed");
        }
        filter = Fixture();
        for (var i = 0; i < 3; i++)
            filter.Observe(Sample(.5f + i * .25f) with { X = .1f }, false, i * 125);
        check(filter.GetGain(default, new(.1f, 1, 0), 250) == 1, "A profile with no X motion suppressed an unknown X contribution");
        filter = Match(); filter.Observe(Sample(1) with { Z = .01f }, false, 251);
        check(filter.GetGain(default, new(0, 1, .01f), 251) == 1, "Unknown Z motion was suppressed by an XY profile");
        var ambiguous = new float[513];
        for (var i = 0; i < ambiguous.Length; i++) ambiguous[i] = i % 2 == 0 ? -1 : 1;
        filter = Fixture(y: ambiguous);
        for (var i = 0; i < 3; i++) filter.Observe(Sample(.001f), false, i * 10);
        check(filter.GetGain(default, new(0, .001f, 0), 20) == 1,
            "Candidate-capacity exhaustion discarded ambiguity and suppressed an unconfirmed sequence");
        filter = Fixture(position: true);
        filter.Observe(Sample(.5f), false, 0); filter.Observe(Sample(.75f), false, 125); filter.Observe(Sample(1), false, 250);
        check(filter.GetGain(default, new(0, 1, 0), 250) == 1, "Position profile matched a rotation channel");
    }

    private static void CheckSmallPhaseJitter(Action<bool, string> check)
    {
        var filter = Fixture(); filter.Observe(Sample(.5f), false, 0);
        _ = filter.GetGain(default, new(0, .5f, 0), 0);
        filter.Observe(Sample(.81f), false, 125);
        check(filter.GetGain(default, new(0, .81f, 0), 125) == 0,
            "Small phase jitter rejected the fixed trajectory and caused a pulse");
        filter.Observe(Sample(1), false, 250);
        check(filter.GetGain(default, new(0, 1, 0), 250) == 0,
            "The corrected phase failed to continue on the fixed profile");
        filter = Fixture(); filter.Observe(Sample(.5f), false, 0);
        _ = filter.GetGain(default, new(0, .5f, 0), 0);
        filter.Observe(Sample(.84f), false, 125);
        check(filter.GetGain(default, new(0, .84f, 0), 125) == 1,
            "Phase correction exceeded its bounded update allowance");
    }

    private static void CheckNativeRealign(Action<bool, string> check)
    {
        var filter = Fixture(); filter.Observe(Sample(.5f), false, 0);
        _ = filter.GetGain(default, new(0, .5f, 0), 0);
        filter.Realign(1, 101, 201, 100);
        check(filter.GetGain(default, new(0, .5f, 0), 100) == 0,
            "A native phase reset leaked the still-current vehicle sample before its next setter");
        filter.Observe(Sample(-.5f), false, 125);
        check(filter.GetGain(default, new(0, -.5f, 0), 125) == 0,
            "A native phase jump leaked vibration by checking the previous phase's wall clock");
        filter.Observe(Sample(-.75f), false, 250); filter.Observe(Sample(-1), false, 375);
        check(filter.GetGain(default, new(0, -1, 0), 375) == 0,
            "The fixed vehicle profile failed to continue after native phase realignment");
        filter.Realign(1, 101, 201, 400); filter.Observe(Sample(2), false, 410);
        check(filter.GetGain(default, new(0, 2, 0), 410) == 1,
            "A native reset classified unknown motion as a known vehicle profile");
        filter = Match(); filter.Realign(2, 102, 202, 251);
        check(filter.GetGain(default, new(0, 1, 0), 251) == 0,
            "Resetting a different native source discarded an existing vehicle match");
    }

    private static void CheckAccounting(Action<bool, string> check)
    {
        var filter = Match(); filter.Observe(Sample(1.2f, 2, 1), false, 250);
        check(filter.GetGain(default, new(0, 2.2f, 0), 250) == 1, "An unknown overlapping slot was suppressed with the known writer");
        filter = Match(); filter.Observe(Sample(.2f, 2), false, 250);
        check(filter.GetGain(default, new(0, .2f, 0), 250) == 0, "A new vehicle track leaked its first plausible sample");
        filter.Observe(Sample(.9f, 2), false, 251);
        check(filter.GetGain(default, new(0, .9f, 0), 251) == 1, "An overwritten slot inherited the prior source's confirmed match");
        filter = Match(); filter.Observe(Sample(.5f, 2, 1), false, 250);
        check(filter.GetGain(default, new(0, 1.5f, 0), 250) == 0,
            "The first matching vehicle overlay caused a pulse over an already-filtered base track");
        filter = Match();
        check(filter.GetGain(default, new(0, 1.01f, 0), 250) == 1 &&
            filter.GetGain(default, new(0, 1, 0), 250) == 1, "Unaccounted aggregate motion retained suppression");
        filter = Match(position: true);
        check(filter.GetGain(new(0, 1, 0), default, 250) == 0, "Known position channel did not match independently");
    }

    private static void CheckUncertainty(Action<bool, string> check)
    {
        var filter = Match(); filter.Invalidate();
        check(filter.GetGain(default, new(0, 1, 0), 250) == 1, "Invalidation retained a known match");
        filter = Match(); check(filter.GetGain(default, new(0, 1, 0), 451) == 1, "Stale observations retained suppression");
        filter = Match(); filter.Observe(Sample(1), false, 451);
        check(filter.GetGain(default, new(0, 1, 0), 451) == 0, "A fresh matching sample after a gap leaked a reacquisition pulse");
        filter = Match(); check(filter.GetGain(default, new(0, 1, 0), 249) == 1, "Backward time retained a match");
        filter = Match(); filter.Observe(Sample(1) with { Scoped = false }, false, 250);
        check(filter.GetGain(default, new(0, 1, 0), 250) == 1, "An unscoped write retained suppression");
        filter = Match(); filter.Observe(Sample(float.NaN), false, 250);
        check(filter.GetGain(default, new(0, 1, 0), 250) == 1, "Nonfinite native evidence retained suppression");
        filter = Match(); filter.Observe(Sample(2) with { Context = 999 }, false, 250);
        check(filter.GetGain(default, new(0, 2, 0), 250) == 1, "Changed source context inherited old phase hypotheses");
        filter = Match();
        for (var source = 2; source <= 33; source++) filter.Observe(Sample(0, source), true, 250);
        check(filter.GetGain(default, new(0, 1, 0), 250) == 1, "Capacity exhaustion retained an old match after evidence loss");
        filter = Match(); var captured = filter;
        var gate = typeof(KnownMotionFilter).GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(filter)!;
        lock (gate)
        {
            var task = Task.Run(() => captured.Observe(Sample(1), false, 250));
            check(task.Wait(TimeSpan.FromSeconds(5)), "Contended native matcher callback blocked");
        }
        check(filter.GetGain(default, new(0, 1, 0), 250) == 1, "Contention loss retained an established match");
    }

    private static void CheckNonloopAndImmutableBank(Action<bool, string> check)
    {
        var values = new[] { 0f, 1f, 0f, -1f, 0f };
        var filter = Fixture(y: values); Array.Fill(values, 99f);
        filter.Observe(Sample(.5f), false, 0); filter.Observe(Sample(.75f), false, 125); filter.Observe(Sample(1), false, 250);
        check(filter.GetGain(default, new(0, 1, 0), 250) == 0, "A caller mutated an immutable profile after construction");
        filter = Fixture(loop: false, seconds: 1, y: new[] { 0f, 1f, 0f });
        filter.Observe(Sample(.8f), false, 0); filter.Observe(Sample(.6f), false, 100); filter.Observe(Sample(.4f), false, 200);
        check(filter.GetGain(default, new(0, .4f, 0), 200) == 0, "A timestamp-consistent short nonloop burst did not match");
        filter.Observe(Sample(.2f), false, 300); filter.Observe(Sample(0), false, 400);
        filter.Observe(Sample(.2f), false, 500);
        check(filter.GetGain(default, new(0, .2f, 0), 500) == 1, "A completed nonloop profile wrapped and suppressed a new burst immediately");
    }

    private static void CheckAllocations(Action<bool, string> check)
    {
        var filter = Fixture(); var step = 0;
        Action callback = () =>
        {
            var phase = (step % 16) / 16f;
            var value = phase < .25f ? phase * 4 : phase < .75f ? 2 - phase * 4 : phase * 4 - 4;
            var clock = step++ * 125L;
            filter.Observe(Sample(value), false, clock);
            _ = filter.GetGain(default, new(0, value, 0), clock);
        };
        var allocated = AllocationChecks.MeasureAllocations(callback, warmup: 100, iterations: 1000);
        check(allocated == 0, $"Known-profile observe/query callbacks allocated {allocated} bytes");
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 10000; i++) callback();
        timer.Stop();
        Console.WriteLine($"Known-motion steady synthetic observe/query: {timer.Elapsed.TotalMicroseconds / 10000:F3} us/call.");
        var bank = new KnownMotionFilter(1000); long seedClock = 0;
        Action seed = () =>
        {
            bank.Observe(Sample(1000000), true, ++seedClock);
            _ = bank.GetGain(new(0, 1000000, 0), default, seedClock);
        };
        var seedBytes = AllocationChecks.MeasureAllocations(seed, warmup: 100, iterations: 1000);
        check(seedBytes == 0, $"Out-of-range fixed-bank rejection allocated {seedBytes} bytes");
        timer.Restart();
        for (var i = 0; i < 1000; i++) seed();
        timer.Stop();
        Console.WriteLine($"Known-motion out-of-range position-bank rejection: {timer.Elapsed.TotalMicroseconds / 1000:F3} us/call; synthetic, no game FPS claim.");
        var zeroBank = new KnownMotionFilter(1000); long zeroClock = 0; var zeroGain = 0f;
        Action zero = () =>
        {
            zeroBank.Observe(Sample(0), true, ++zeroClock);
            zeroGain = zeroBank.GetGain(default, default, zeroClock);
        };
        var zeroBytes = AllocationChecks.MeasureAllocations(zero, warmup: 100, iterations: 1000);
        check(zeroBytes == 0 && zeroGain == 1, $"Common zero-channel callback allocated {zeroBytes} bytes or suppressed absent motion");
        timer.Restart();
        for (var i = 0; i < 10000; i++) zero();
        timer.Stop();
        Console.WriteLine($"Known-motion common zero channel: {timer.Elapsed.TotalMicroseconds / 10000:F3} us/call.");

        var inRangeBank = new KnownMotionFilter(1000); long inRangeClock = 0;
        Action inRange = () =>
        {
            // Force fresh hypotheses inside the recorded vertical amplitude range.
            // This advisory includes invalidation cost; it is not steady matching.
            inRangeBank.Invalidate();
            inRangeBank.Observe(Sample(.0001f), true, ++inRangeClock);
            _ = inRangeBank.GetGain(new(0, .0001f, 0), default, inRangeClock);
        };
        var rangeBytes = AllocationChecks.MeasureAllocations(inRange, warmup: 100, iterations: 1000);
        check(rangeBytes == 0, $"In-range fixed-bank phase seeding allocated {rangeBytes} bytes");
        timer.Restart();
        for (var i = 0; i < 1000; i++) inRange();
        timer.Stop();
        Console.WriteLine($"Known-motion in-range position-bank fresh seed including invalidation: {timer.Elapsed.TotalMicroseconds / 1000:F3} us/call.");

        var dense = new float[513];
        for (var i = 0; i < dense.Length; i++) dense[i] = i % 2 == 0 ? -1 : 1;
        var ambiguous = Fixture(y: dense); long ambiguousClock = 0; var ambiguousGain = 0f;
        Action worst = () =>
        {
            ambiguous.Observe(Sample(.001f), false, ++ambiguousClock);
            ambiguousGain = ambiguous.GetGain(default, new(0, .001f, 0), ambiguousClock);
        };
        var worstBytes = AllocationChecks.MeasureAllocations(worst, warmup: 100, iterations: 1000);
        check(worstBytes == 0 && ambiguousGain == 1,
            $"Candidate-capacity in-range rejection allocated {worstBytes} bytes or suppressed ambiguity");
        timer.Restart();
        for (var i = 0; i < 1000; i++) worst();
        timer.Stop();
        Console.WriteLine($"Known-motion synthetic in-range candidate-capacity seed: {timer.Elapsed.TotalMicroseconds / 1000:F3} us/call; advisory only.");

    }
}
