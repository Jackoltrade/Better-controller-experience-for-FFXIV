// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Text.Json;
using DualSenseLighting;
using DualSenseLighting.Core;

internal static class RumblePulseChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var pulse = new RumblePulse();
        foreach (var milliseconds in new[] { 60, 120, 300 })
        {
            pulse.Reset();
            check(pulse.Update(0.7f, 0, milliseconds) == 0.7f, "The impact onset was delayed or lost intensity");
            check(pulse.Update(0.7f, milliseconds / 2000.0, milliseconds) == 0.7f,
                "The first part of an impact lost its stable amplitude");
            var tail = pulse.Update(0.7f, milliseconds / 1000.0 * 0.9, milliseconds);
            check(tail is > 0 and < 0.7f && pulse.Update(0.7f, milliseconds / 1000.0, milliseconds) == 0,
                "Pulse duration did not produce a bounded faded stop");
            for (var index = 1; index <= 100; index++)
                check(pulse.Update(0.7f, milliseconds / 1000.0 + index * 0.033, milliseconds) == 0,
                    "A held sound repeatedly buzzed without a new onset");
        }
        pulse.Reset();
        for (var index = 0; index < 100; index++)
        {
            var input = index % 2 == 0 ? 0.4f : 0.46f;
            var value = pulse.Update(input, index * 0.033, 120);
            check(index < 4 || value == 0, "Small SFX fluctuations retriggered rapid impact pulses");
        }
        pulse.Reset();
        for (var attack = 0; attack < 4; attack++)
        {
            var start = attack * 2.5;
            check(pulse.Update(0.6f, start, 120) == 0.6f, "Repeated GCD impacts weakened or lost their onset");
            pulse.Update(0, start + 0.15, 120);
            pulse.Update(0, start + 0.5, 120);
        }
        pulse.Reset();
        pulse.Update(0.2f, 0, 120);
        check(pulse.Update(0.6f, 0.033, 120) == 0.6f && pulse.Update(0.8f, 0.1, 120) < 0.6f &&
            pulse.Update(0.8f, 0.13, 120) == 0, "Layered sounds extended or rapidly restarted an existing pulse");
        check(pulse.Update(0.1f, 0.3, 120) == 0 && pulse.Update(0.6f, 0.4, 120) == 0.6f,
            "A distinct later impact could not retrigger after the minimum separation");
        check(pulse.Update(1, double.NaN, 120) == 0 && pulse.Update(1, -1, 120) == 0 &&
            pulse.Update(float.NaN, 1, 120) == 0 && pulse.Update(1, 0.5, 120) == 0,
            "Invalid or backward pulse input was accepted");
        pulse.Reset();
        check(pulse.Update(0, 1, 120) == 0, "Reset replayed a previous effect");
        var previousLow = 0; var previousHigh = 0;
        for (var percent = 0; percent <= 100; percent++)
        {
            var motors = RumbleMotors.FromImpact(percent / 100f);
            check(motors.Low >= previousLow && motors.High >= previousHigh && motors.High <= motors.Low * 0.25f + 1,
                "Basic rumble intensity is binary/non-monotonic or overdrives the fast motor");
            previousLow = motors.Low; previousHigh = motors.High;
        }
        check(RumbleMotors.FromImpact(float.NaN) == default && RumbleMotors.FromImpact(-1) == default,
            "Invalid impact strength reached the motors");
        var config = JsonSerializer.Deserialize<Configuration>("{}")!;
        check(config.RumbleStrength == 0.5f && config.RumblePulseMilliseconds == 120 && config.BluetoothMotorBalance == 0.85f,
            "Old configurations did not receive the recommended basic-rumble defaults");
        check(new HapticSettings().BluetoothMotorBalance == 0.85f && config.HapticsSnapshot().BluetoothMotorBalance == 0.85f &&
            JsonSerializer.Deserialize<Configuration>("{\"BluetoothMotorBalance\":0}")!.HapticsSnapshot().BluetoothMotorBalance == 0,
            "Bluetooth defaults did not reach snapshots or replaced an explicitly saved neutral balance");
        config.RumbleStrength = 0.32f; config.RumblePulseMilliseconds = 200; config.BluetoothMotorBalance = 0.6f;
        var reloaded = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;
        check(reloaded.HapticsSnapshot() is { RumbleStrength: 0.32f, RumblePulseMilliseconds: 200, BluetoothMotorBalance: 0.6f },
            "Basic-rumble strength or duration failed to persist");
        config.ResetHapticsToDefaults();
        check(config.RumbleStrength == 0.5f && config.RumblePulseMilliseconds == 120 && config.BluetoothMotorBalance == 0.85f &&
            new HapticSettings(RumbleStrength: float.NaN, RumblePulseMilliseconds: -1).Normalize() is
                { RumbleStrength: 0.5f, RumblePulseMilliseconds: 60 } &&
            new HapticSettings(RumbleStrength: 2, RumblePulseMilliseconds: 999).Normalize() is
                { RumbleStrength: 1, RumblePulseMilliseconds: 300 },
            "Basic-rumble reset or invalid-value limits failed");
        pulse.Reset(); pulse.Update(0.5f, 0, 120);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 1; index <= 10000; index++) pulse.Update(0.5f, index * 0.033, 120);
        check(GC.GetAllocatedBytesForCurrentThread() == allocated, "The pulse renderer allocated during regular updates");
    }
}
