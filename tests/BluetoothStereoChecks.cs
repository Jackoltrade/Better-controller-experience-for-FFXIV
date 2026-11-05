// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
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
            var left = output.RumbleLeft.TakePeak(); var right = output.RumbleRight.TakePeak();
            session.Tick(output.RumbleSignal, output.CurrentSettings, true, index, stereo: (left, right));
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
        check(output.RumbleSignal.TakePeak() == 0 && output.RumbleLeft.TakePeak() == 0 && output.RumbleRight.TakePeak() == 0,
            "A stale basic-rumble frame crossed a stereo/controller generation reset");
        output.Configure(settings, true, true, true, true, false);
        check(!output.CurrentSettings.BluetoothRumble && !output.RumbleLeft.Active && !output.RumbleRight.Active,
            "Switching to XInput retained a Bluetooth spatial signal");
        check(pcm.SequenceEqual(original), "Stereo rumble changed the game's original PCM buffer");
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
            output.RumbleLeft.TakePeak() == 0 && output.RumbleRight.TakePeak() == 0,
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
