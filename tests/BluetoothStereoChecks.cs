// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static unsafe class BluetoothStereoChecks
{
    private sealed class Devices : IRumbleDevices
    {
        public RumbleMotors Motors;
        public bool IsConnected(int slot) => slot == 0;
        public bool SetVibration(int slot, RumbleMotors motors) { Motors = motors; return slot == 0; }
    }

    internal static void Run(Action<bool, string> check)
    {
        CheckBalance(check);
        CheckDelayedSides(check);
        CheckAtomicSignal(check);
        CheckConfigurationKeys(check);
        var output = new NativeHapticOutput(null, 0, 0, 0);
        var settings = new HapticSettings(Enabled: true, Strength: 0.5f, Spatial: true, DistanceFalloff: false,
            FollowImpact: false, RumbleStrength: 1, BluetoothMotorBalance: 0);
        output.SetSpatialMatrices(new float[16], new float[32]);
        var pcm = new float[2048]; Array.Fill(pcm, 0.1f);
        var original = (float[])pcm.Clone();
        var devices = new Devices();
        using var session = new RumbleSession(devices, () => false);
        for (var index = 0; index < 4; index++)
        {
            var requested = settings with { Spatial = index < 2, DistanceFalloff = index == 3 };
            session.Release(); session.Select(HapticOutputMode.XInput, 0, true, index);
            output.ResetRumbleSignals(); output.Configure(requested, true, true, true, true, true);
            output.SetListener(new(Vector3.Zero, index == 1 ? -Vector3.UnitX : Vector3.UnitX, Stopwatch.GetTimestamp()));
            var frame = output.Prepare(null, true, 2, 0, index == 3 ? new(0, 0, 14) : new(3, 0, 0));
            fixed (float* samples = pcm) output.Mix(frame, samples, pcm.Length, 0);
            session.Tick(output.RumbleSignal, output.CurrentSettings, true, index, stereo: output.RumbleStereo.TakePeak());
            var full = RumbleMotors.FromLevel(0.4f).Low;
            var half = RumbleMotors.FromLevel(0.2f).Low;
            var expected = index switch
            {
                0 => new RumbleMotors(0, full),
                1 => new RumbleMotors(full, 0),
                2 => new RumbleMotors(full, full),
                _ => new RumbleMotors(half, half),
            };
            check(frame.HasOutput && output.CurrentSettings.BluetoothRumble && devices.Motors == expected,
                "Bluetooth grip balance did not follow camera facing, spatial off or the shared distance fade");
        }
        output.ResetRumbleSignals(); output.Configure(settings with { DistanceFalloff = true }, true, true, true, true, true);
        output.SetListener(new(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp()));
        check(!output.Prepare(null, true, 2, 0, new(0, 0, 25)).HasOutput,
            "Bluetooth stereo feedback bypassed the common distance cutoff");
        var old = output.Prepare(null, true, 2, 0, new(3, 0, 0));
        output.ResetRumbleSignals(); output.Configure(settings with { DistanceFalloff = true }, true, true, true, true, true);
        fixed (float* samples = pcm) output.Mix(old, samples, pcm.Length, 0);
        check(output.RumbleSignal.TakePeak() == 0 && output.RumbleStereo.TakePeak() == default,
            "A stale basic-rumble frame crossed a stereo/controller generation reset");
        output.Configure(settings, true, true, true, true, false);
        check(!output.CurrentSettings.BluetoothRumble && !output.RumbleStereo.Active,
            "Switching to XInput retained a Bluetooth spatial signal");
        check(pcm.SequenceEqual(original), "Stereo rumble changed the game's original PCM buffer");
    }

    private static void CheckAtomicSignal(Action<bool, string> check)
    {
        var signal = new StereoRumbleSignal();
        var inactive = signal.CurrentEpoch;
        signal.Observe(1, 1, inactive);
        check(signal.TakePeak() == default, "Inactive stereo signal accepted output");
        signal.SetActive(true);
        var epoch = signal.CurrentEpoch;
        signal.Observe(0.12345679f, 0.8754321f, epoch);
        check(signal.TakePeak() == (0.12345679f, 0.8754321f) && signal.TakePeak() == default,
            "Stereo handoff quantized, swapped, or replayed a pair");
        signal.Observe(0.25f, 0.75f, epoch); signal.Observe(0.5f, 0.125f, epoch);
        check(signal.TakePeak() == (0.5f, 0.75f), "Stereo interval did not merge independent channel peaks");
        signal.Observe(float.NaN, 1, epoch); signal.Observe(1, float.PositiveInfinity, epoch);
        check(signal.TakePeak() == default, "Invalid stereo pair became a one-sided pulse");
        signal.Observe(-1, 2, epoch);
        check(signal.TakePeak() == (0, 1), "Stereo bounds did not clamp to normalized endpoints");
        signal.Observe(-0.0f, -0.0f, epoch); signal.Observe(0.25f, 0.5f, epoch);
        check(signal.TakePeak() == (0.25f, 0.5f), "Negative zero suppressed a later positive stereo peak");
        signal.SetActive(false); signal.SetActive(true);
        signal.Observe(1, 1, epoch);
        check(signal.TakePeak() == default, "A retired stereo epoch republished into a new controller generation");
        epoch = signal.CurrentEpoch;
        // Start publication and consumption together. Either whole-pair ordering
        // is legal; a split interval is never legal.
        using var start = new Barrier(2);
        var publisher = Task.Run(() =>
        {
            for (var index = 0; index < 10000; index++)
            {
                start.SignalAndWait(); signal.Observe(0.5f, 0.5f, epoch); start.SignalAndWait();
            }
        });
        var coherent = true;
        for (var index = 0; index < 10000; index++)
        {
            start.SignalAndWait(); var first = signal.TakePeak(); start.SignalAndWait(); var second = signal.TakePeak();
            coherent &= first.Left == first.Right && second.Left == second.Right && first.Left + second.Left == 0.5f;
        }
        publisher.GetAwaiter().GetResult();
        check(coherent, "One balanced stereo block split across worker ticks");
        for (var index = 0; index < 1000; index++) { signal.Observe(0.5f, 0.25f, epoch); signal.TakePeak(); }
        Action observeAndTake = () => { signal.Observe(0.5f, 0.25f, epoch); signal.TakePeak(); };
        var allocated = AllocationChecks.MeasureAllocations(observeAndTake, warmup: 0, iterations: 10000);
        check(allocated == 0, $"Stereo publication/consumption allocated {allocated} managed bytes");
    }

    private static void CheckConfigurationKeys(Action<bool, string> check)
    {
        var requested = new HapticSettings(Enabled: true, Strength: 0.5f, FootstepAmplifier: true,
            BossEnabled: true, RegularEnemyEnabled: true, HitReactions: true, ScreenEffects: true);
        var output = new NativeHapticOutput(null, 0, 0, 0);
        output.SetEnemyContext(true);
        // Reflection varies every public init property, including future ones.
        // A newly added setting omitted by the explicit key therefore fails this
        // behavior test instead of silently retaining stale prepared frames.
        foreach (var property in typeof(HapticSettings).GetProperties().Where(p => p.SetMethod?.IsPublic == true))
        {
            var changed = requested with { };
            var original = property.GetValue(requested)!;
            object next = original switch
            {
                bool value => !value,
                float value => value + 0.01f,
                int value => value + 1,
                Enum value => Enum.GetValues(value.GetType()).Cast<object>().First(other => !other.Equals(value)),
                _ => throw new InvalidOperationException("Add a setting mutation for " + property.Name),
            };
            property.SetValue(changed, next);
            output.Configure(requested, true, true);
            var before = output.CurrentSettings; var screenBefore = output.ScreenSettings;
            output.Configure(changed, true, true);
            check(!ReferenceEquals(before, output.CurrentSettings),
                $"Changing {property.Name} left stale effective settings/frames");
            var resetsScreen = property.Name is nameof(HapticSettings.ScreenEffects) or nameof(HapticSettings.ScreenStrength) or
                nameof(HapticSettings.OutputMode) or nameof(HapticSettings.RumbleController);
            check(ReferenceEquals(screenBefore, output.ScreenSettings) != resetsScreen,
                $"Changing {property.Name} used the wrong independent screen reset policy");
        }
        foreach (var bluetooth in new[] { false, true })
        {
            output.Configure(requested, true, true, useRumble: false);
            var screenBefore = output.ScreenSettings;
            output.Configure(requested, true, true, useRumble: true, stereoRumble: bluetooth);
            check(!ReferenceEquals(screenBefore, output.ScreenSettings), "Backend selection failed to reset screen routing");
        }
        output.Configure(requested, true, true);
        for (var index = 0; index < 1000; index++) output.Configure(requested, true, true);
        var snapshot = output.CurrentSettings; var screenSnapshot = output.ScreenSettings;
        Action configure = () => output.Configure(requested, true, true);
        var allocation = AllocationChecks.MeasureAllocations(configure, warmup: 0, iterations: 10000);
        check(allocation == 0 && ReferenceEquals(snapshot, output.CurrentSettings) && ReferenceEquals(screenSnapshot, output.ScreenSettings),
            $"Unchanged haptic settings reset snapshots or allocated {allocation} bytes");
    }

    private static void CheckBalance(Action<bool, string> check)
    {
        var motors = new RumbleMotors(65535, 32768);
        check(motors.WithBalance(0) == motors && motors.WithBalance(0.5f) == new RumbleMotors(32768, 32768) &&
            motors.WithBalance(-0.5f) == new RumbleMotors(65535, 16384),
            "Bluetooth motor balance scaled the wrong channel or amplified output");
        check(motors.WithBalance(2) == new RumbleMotors(0, 32768) &&
            motors.WithBalance(-2) == new RumbleMotors(65535, 0) &&
            motors.WithBalance(float.NaN) == motors && motors.WithBalance(float.PositiveInfinity) == motors &&
            default(RumbleMotors).WithBalance(0.5f) == default,
            "Bluetooth motor balance accepted invalid output or lost silence");
        check(new HapticSettings(BluetoothMotorBalance: 2).Normalize().BluetoothMotorBalance == 1 &&
            new HapticSettings(BluetoothMotorBalance: -2).Normalize().BluetoothMotorBalance == -1 &&
            new HapticSettings(BluetoothMotorBalance: float.NaN).Normalize().BluetoothMotorBalance == 0.85f,
            "Bluetooth motor-balance configuration did not normalize invalid values");

        var devices = new Devices();
        using var session = new RumbleSession(devices, () => false);
        var signal = new SfxLoudness(); signal.SetActive(true);
        var settings = new HapticSettings(Enabled: true, RumbleStrength: 1, DistanceFalloff: false, BluetoothMotorBalance: 0.5f)
            { BluetoothRumble = true };
        session.Select(HapticOutputMode.XInput, 0, true, 0);
        signal.Observe(new float[] { 0.5f });
        session.Tick(signal, settings, true, 0, stereo: (0, 0.5f));
        check(devices.Motors == new RumbleMotors(0, 32768), "Motor calibration introduced vibration on the silent spatial side");
        session.Tick(signal, settings, true, 0.2, stereo: (0, 0));
        session.Tick(signal, settings, true, 1, stereo: (0.5f, 0));
        check(devices.Motors == new RumbleMotors(16384, 0), "Motor calibration lost the isolated left direction");

        session.Release(); session.Select(HapticOutputMode.XInput, 0, true, 1.5);
        session.RequestConfirmation(1.5);
        session.Tick(signal, settings, true, 1.5, stereo: (0, 0));
        check(devices.Motors == new RumbleMotors(8192, 16384), "Bluetooth enable confirmation bypassed motor calibration");

        session.Release(); session.Select(HapticOutputMode.XInput, 0, true, 3);
        signal.Observe(new float[] { 0.5f });
        session.Tick(signal, settings with { BluetoothRumble = false, ScreenEffects = true }, true, 3, screenLevel: 0.5f);
        check(devices.Motors == RumbleMotors.Merge(default, RumbleMotors.FromImpact(0.5f), RumbleMotors.FromLevel(0.5f)),
            "Bluetooth balance modified XInput output");

        var output = new NativeHapticOutput(null, 0, 0, 0);
        output.Configure(settings, true, true, true, true, true);
        var old = output.Prepare(null, true, 2, 0);
        check(old.HasOutput, "Motor-balance generation test did not prepare a real output frame");
        output.Configure(settings with { BluetoothMotorBalance = -0.5f }, true, true, true, true, true);
        var samples = new float[2048]; Array.Fill(samples, 0.1f);
        fixed (float* pcm = samples) output.Mix(old, pcm, samples.Length, 0);
        check(output.CurrentSettings.BluetoothMotorBalance == -0.5f && output.RumbleSignal.TakePeak() == 0 &&
            output.RumbleStereo.TakePeak() == default,
            "Changing motor balance retained stale settings or an in-flight audio frame");
    }

    private static void CheckDelayedSides(Action<bool, string> check)
    {
        foreach (var firstLeft in new[] { true, false })
        {
            var devices = new Devices();
            using var session = new RumbleSession(devices, () => false);
            var signal = new SfxLoudness(); signal.SetActive(true);
            var settings = new HapticSettings(Enabled: true, RumbleStrength: 1, BluetoothMotorBalance: 0) { BluetoothRumble = true };
            session.Select(HapticOutputMode.XInput, 0, true, 0);
            signal.Observe(new float[] { 0.8f });
            session.Tick(signal, settings, true, 0, stereo: firstLeft ? (0.8f, 0) : (0, 0.8f));
            session.Tick(signal, settings, true, 0.2, stereo: (0, 0));

            // Force the audio/worker interleaving: scalar arrives after the
            // sides were consumed, and the side values arrive on the next tick.
            signal.Observe(new float[] { 0.2f });
            session.Tick(signal, settings, true, 1, stereo: (0, 0));
            check(devices.Motors == default,
                "Bluetooth started a pulse with a previous sound's direction before its own sides arrived");
            session.Tick(signal, settings, true, 1.033, stereo: firstLeft ? (0, 0.2f) : (0.2f, 0));
            var command = RumbleMotors.FromLevel(0.2f).Low;
            check(devices.Motors == (firstLeft ? new RumbleMotors(0, command) : new RumbleMotors(command, 0)),
                "A delayed quieter impact retained the louder previous impact's grip balance");

            session.Tick(signal, settings, true, 1.3, stereo: (0, 0));
            signal.Observe(new float[] { 0.2f });
            session.Tick(signal, settings, true, 1.4, stereo: (0, 0));
            check(devices.Motors == default, "A late scalar replayed a completed Bluetooth stereo pulse");
        }
    }
}
