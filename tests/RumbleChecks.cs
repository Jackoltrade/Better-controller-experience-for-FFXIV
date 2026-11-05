// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using DualSenseLighting;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static unsafe class RumbleChecks
{
    private sealed class Devices : IRumbleDevices
    {
        public readonly bool[] Connected = new bool[4];
        public readonly RumbleMotors[] Game = new RumbleMotors[4];
        public readonly RumbleMotors[] Plugin = new RumbleMotors[4];
        public readonly RumbleMotors[] Screen = new RumbleMotors[4];
        public readonly List<(int Slot, RumbleMotors Motors)> Writes = new();
        public bool IsConnected(int slot) => Connected[slot];
        public bool SetVibration(int slot, RumbleMotors motors) => SetVibration(slot, motors, default);
        public bool SetVibration(int slot, RumbleMotors motors, RumbleMotors screen)
        {
            Plugin[slot] = motors; Screen[slot] = screen;
            Writes.Add((slot, RumbleMotors.Merge(Game[slot], motors, screen)));
            return Connected[slot];
        }
        public void ApplyGame(int slot, RumbleMotors motors)
        {
            Game[slot] = motors;
            Writes.Add((slot, RumbleMotors.Merge(motors, Plugin[slot], Screen[slot])));
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        CheckAudioRouting(check);
        CheckScreenCoexistence(check);
        CheckStrengthTest(check);
        var config = new Configuration();
        check(config.HapticsSnapshot().OutputMode == HapticOutputMode.Automatic && config.RumbleController == -1,
            "Existing configs must use automatic output and first connected controller");
        config.HapticOutput = HapticOutputMode.XInput; config.RumbleController = 2;
        check(config.HapticsSnapshot() is { OutputMode: HapticOutputMode.XInput, RumbleController: 2 }, "Output selection was not saved in the worker snapshot");
        config.ResetHapticsToDefaults();
        check(config.HapticOutput == HapticOutputMode.Automatic && config.RumbleController == -1, "Haptic reset retained an output override");
        check((new HapticSettings(OutputMode: (HapticOutputMode)999, RumbleController: 99).Normalize()) is
            { OutputMode: HapticOutputMode.Automatic, RumbleController: 3 }, "Invalid output settings were not normalized");
        check(RumbleMotors.Merge(new(40000, 10000), new(20000, 30000)) == new RumbleMotors(40000, 30000) &&
            RumbleMotors.Merge(new(123, 456), default) == new RumbleMotors(123, 456),
            "Rumble mixing must preserve each native motor and restore it when the plugin stops");
        check(RumbleMotors.Merge(new(40000, 10000), new(20000, 30000), new(10000, 20000)) == new RumbleMotors(50000, 50000) &&
            RumbleMotors.Merge(new(65000, 0), new(1000, 60000), new(1000, 6000)) == new RumbleMotors(65535, 65535) &&
            RumbleMotors.Merge(default, default, new(30000, 40000)) == new RumbleMotors(30000, 40000),
            "Independent screen rumble must add to the existing game/SFX blend and saturate without overflow");
        check(RumbleMotors.FromLevel(float.NaN) == default && RumbleMotors.FromLevel(-1) == default &&
            RumbleMotors.FromLevel(5) == new RumbleMotors(65535, 65535), "Motor conversion is not bounded");
        check(RumbleMotors.Measure(1, 0.25f, default) == 0.25f &&
            RumbleMotors.Measure(1, 0.25f, new(true, 1, 5, 0, 0.5f)) == 0.125f,
            "Regular rumble ignored the strength limit or distance fade");

        var devices = new Devices(); devices.Connected[1] = devices.Connected[3] = true;
        var ds5 = true;
        using var session = new RumbleSession(devices, () => ds5);
        var signal = new SfxLoudness(); signal.SetActive(true);
        var settings = new HapticSettings(Enabled: true, RumbleStrength: 1);
        void Publish(float level) { signal.Observe(new[] { level }); }
        session.Select(HapticOutputMode.Automatic, -1, true, 0);
        Publish(0.5f); session.Tick(signal, settings, true, 0);
        check(!session.UsesRumble && session.Slot == -1 && devices.Writes.Count == 0,
            "Automatic sent ordinary rumble with a DualSense connected");
        ds5 = false;
        session.Select(HapticOutputMode.Automatic, -1, true, 1);
        Publish(0.5f); session.Tick(signal, settings, true, 1);
        check(session.UsesRumble && session.Slot == 1 && devices.Writes[^1] == (1, RumbleMotors.FromImpact(0.5f)),
            "Automatic did not drive only the first connected XInput controller");
        session.Tick(signal, settings, true, 1.1);
        check(devices.Writes[^1].Motors.Low > 0 && devices.Writes[^1].Motors.Low < 32768, "The bounded impact did not fade before its deadline");
        session.Tick(signal, settings, true, 2);
        check(devices.Writes[^1].Motors == default, "Silence left motors running");
        Publish(1); session.Tick(signal, settings, true, 2.1);
        var count = devices.Writes.Count;
        session.Tick(signal, settings, false, 2.2);
        check(devices.Writes.Count == count + 1 && devices.Writes[^1].Motors == default, "Background/master-off did not release rumble immediately");
        session.Tick(signal, settings, true, 2.3);
        check(devices.Writes[^1].Motors == default, "Feedback replayed after resuming");
        Publish(1); session.Tick(signal, settings, true, 2.4);
        session.Select(HapticOutputMode.XInput, 3, true, 2.5);
        check(devices.Writes[^1] == (1, default(RumbleMotors)) && session.Slot == 3, "Controller selection did not release the previous controller");
        session.RequestConfirmation(2.5);
        session.Tick(signal, settings, true, 2.5);
        check(devices.Writes[^1] == (3, RumbleMotors.FromLevel(0.25f)), "Regular rumble confirmation missing its first pulse");
        session.Tick(signal, settings, true, 2.65);
        check(devices.Writes[^1].Motors == default, "Regular rumble confirmation missing its silent gap");
        session.Tick(signal, settings, true, 2.75);
        check(devices.Writes[^1].Motors == RumbleMotors.FromLevel(0.25f), "Regular rumble confirmation missing its second pulse");
        session.Tick(signal, settings, true, 2.85);
        check(devices.Writes[^1].Motors == default, "Regular rumble confirmation repeated");
        devices.Connected[3] = false;
        Publish(1); session.Tick(signal, settings, true, 2.9);
        check(session.Slot == -1, "A failed write retained a disconnected slot");
        devices.Connected[3] = true;
        session.Select(HapticOutputMode.XInput, 3, true, 4);
        check(session.Slot == 3, "Regular rumble did not reconnect");
        ds5 = true;
        session.Select(HapticOutputMode.XInput, 3, true, 5);
        check(session.UsesRumble && session.Slot == 3, "Manual output override ignored an XInput controller in a mixed setup");
        Publish(1); session.Tick(signal, settings, true, 5);
        session.Select(HapticOutputMode.Automatic, -1, true, 6);
        check(!session.UsesRumble && session.Slot == -1 && devices.Writes[^1].Motors == default,
            "Connecting/switching to DualSense did not release ordinary rumble");
        session.Select(HapticOutputMode.XInput, 3, true, 7);
        Publish(1); session.Tick(signal, settings, true, 7);
        session.Dispose();
        check(devices.Writes[^1].Motors == default && session.Slot == -1, "Unloading retained plugin motor output");
    }

    private static void CheckScreenCoexistence(Action<bool, string> check)
    {
        var devices = new Devices(); devices.Connected[0] = devices.Connected[2] = true;
        using var session = new RumbleSession(devices, () => false);
        var signal = new SfxLoudness(); signal.SetActive(true);
        var current = new HapticSettings(Enabled: true, ScreenEffects: true, RumbleStrength: 1);
        session.Select(HapticOutputMode.XInput, 0, true, 1);
        devices.ApplyGame(0, new(40000, 10000));
        signal.Observe(new[] { 0.3f });
        session.Tick(signal, current, true, 1, 0.2f);
        check(devices.Plugin[0] == RumbleMotors.FromImpact(0.3f) && devices.Screen[0] == RumbleMotors.FromLevel(0.2f) &&
            devices.Writes[^1].Motors == new RumbleMotors(53107, 23107),
            "Game or SFX vibration suppressed the current screen sample");

        // Native game commands can arrive between worker ticks. Separate layers
        // keep the current screen sample present through those commands.
        devices.ApplyGame(0, new(1000, 60000));
        check(devices.Writes[^1].Motors == new RumbleMotors(32767, 65535),
            "A later native game command dropped screen rumble or overflowed a motor");
        signal.Observe(new[] { 0.7f });
        session.Tick(signal, current, true, 1.01, 0.8f);
        check(devices.Plugin[0] == RumbleMotors.FromImpact(0.7f) && devices.Screen[0] == RumbleMotors.FromLevel(0.8f) &&
            devices.Writes[^1].Motors == new RumbleMotors(65535, 65535),
            "Stronger SFX displaced screen rumble rather than mixing within the motor range");
        session.Tick(signal, current, true, 1.011, 0);
        check(devices.Screen[0] == default && devices.Plugin[0] != default &&
            devices.Writes[^1].Motors == RumbleMotors.Merge(devices.Game[0], devices.Plugin[0]),
            "Screen vibration retained a release tail or clearing it stopped ongoing SFX/native rumble");

        session.RequestConfirmation(2);
        session.Tick(signal, current, true, 2, 0.6f);
        check(devices.Plugin[0] == RumbleMotors.FromLevel(0.25f) && devices.Screen[0] == RumbleMotors.FromLevel(0.6f),
            "Confirmation vibration suppressed the screen layer");
        session.Tick(signal, current, true, 2.15, 0.4f);
        check(devices.Plugin[0] == default && devices.Screen[0] == RumbleMotors.FromLevel(0.4f),
            "A confirmation gap suppressed current screen movement");
        session.Tick(signal, current, true, 2.151, 0);
        check(devices.Plugin[0] == default && devices.Screen[0] == default && devices.Writes[^1].Motors == devices.Game[0],
            "A zero camera sample replayed screen feedback or removed native vibration");

        session.Tick(signal, current, true, 3, float.NaN);
        check(devices.Screen[0] == default, "An invalid camera level generated rumble");
        session.Tick(signal, current, true, 3.01, 5);
        check(devices.Screen[0] == new RumbleMotors(65535, 65535) && devices.Writes[^1].Motors == new RumbleMotors(65535, 65535),
            "The screen layer exceeded its final motor bound");
        session.Tick(signal, current with { ScreenEffects = false }, true, 3.02, 1);
        check(devices.Screen[0] == default && devices.Writes[^1].Motors == devices.Game[0],
            "A disabled screen setting retained screen rumble");
        session.Tick(signal, current, true, 3.03, 0.8f);
        session.Tick(signal, current, false, 3.04, 0.8f);
        check(devices.Plugin[0] == default && devices.Screen[0] == default && devices.Writes[^1].Motors == devices.Game[0],
            "Background/master-off did not remove independent screen output");
        session.Tick(signal, current, true, 3.05, 0);
        check(devices.Screen[0] == default, "Screen feedback replayed after focus returned");
        session.Tick(signal, current, true, 3.06, 0.8f);
        session.Select(HapticOutputMode.XInput, 2, true, 4);
        check(devices.Screen[0] == default && devices.Plugin[0] == default && session.Slot == 2,
            "Changing controllers retained screen rumble on the old controller");
        session.Tick(signal, current, true, 4, 0.8f);
        devices.Connected[2] = false;
        session.Tick(signal, current, true, 4.01, 0.7f);
        check(session.Slot == -1 && devices.Plugin[2] == default && devices.Screen[2] == default,
            "A failed screen-output write retained a disconnected controller or screen layer");
    }

    private static void CheckAudioRouting(Action<bool, string> check)
    {
        // No vibration graph, native mix function or voice sends: regular rumble
        // must work without touching DualSense's audio destination.
        var output = new NativeHapticOutput(null, 0, 0, 0);
        var settings = new HapticSettings(Enabled: true, Strength: 0.6f, DistanceFalloff: false);
        output.Configure(settings, true, true, true, true);
        output.RequestEnableCue();
        check(!output.HasEnableCue, "Regular rumble queued a second confirmation through the native audio mixer");
        var samples = new float[2048]; Array.Fill(samples, 0.1f);
        var frame = output.Prepare(null, true, 2, 0);
        check(frame.HasOutput && frame.Rumble && frame.Destination == 0, "Regular rumble required a DualSense audio destination");
        fixed (float* pcm = samples) output.Mix(frame, pcm, samples.Length, 0);
        check(Math.Abs(output.RumbleSignal.TakePeak() - 0.48f) < 0.001f, "Player feedback did not become variable-strength rumble");
        var quieter = output.Prepare(null, true, 2, 0);
        output.Configure(settings with { Strength = 0.2f }, true, true, true, true);
        fixed (float* pcm = samples) output.Mix(quieter, pcm, samples.Length, 0);
        check(output.RumbleSignal.TakePeak() == 0, "An old audio frame survived a strength change");
        check(!output.Prepare(null, false, 2, 0).HasOutput && !output.Prepare(null, true, 6, 0).HasOutput &&
            !output.Prepare(null, true, 2, 2).HasOutput, "Rumble admitted unrelated actors, footsteps, or native-only feedback");
        output.SetEnemyContext(true);
        output.SetSpatialMatrices(new float[16], new float[32]);
        output.SetListener(new HapticListenerPose(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp()));
        settings = settings with { BossEnabled = true, EnemyStrength = 0.3f, Spatial = true, DistanceFalloff = true };
        output.Configure(settings, true, true, true, true);
        var boss = output.Prepare(null, false, 2, 0, new Vector3(1, 0, 0), enemy: EnemySoundKind.Boss);
        check(boss.HasOutput && boss.Rumble && boss.Spatial.Valid && boss.Gain == 0.3f, "Boss rumble lost its independent strength/spatial filtering");
        check(!output.Prepare(null, false, 2, 0, new Vector3(26, 0, 0), enemy: EnemySoundKind.Boss).HasOutput &&
            !output.Prepare(null, false, 2, 0, enemy: EnemySoundKind.Boss).HasOutput,
            "Enemy rumble ignored distance cutoff or unknown positions");
        fixed (float* pcm = samples) output.Mix(boss, pcm, samples.Length, 0);
        check(output.RumbleSignal.TakePeak() > 0 && output.BossBlocks == 1, "Boss audio was not routed to rumble");
        settings = settings with { HitReactions = true };
        output.Configure(settings, true, true, true, true);
        var hit = output.Prepare(null, true, 2, 0, Vector3.Zero, hitReaction: true);
        fixed (float* pcm = samples) output.Mix(hit, pcm, samples.Length, 0);
        check(output.RumbleSignal.TakePeak() > 0 && output.HitReactionBlocks == 1, "Player hit reactions lost their rumble route");
        foreach (var mode in new[] { "background", "disabled", "attribution", "layout", "native" })
        {
            output.Configure(settings, true, true, true, true);
            var stale = output.Prepare(null, true, 2, 0, Vector3.UnitX);
            check(stale.HasOutput, "The queued rumble fixture was already silent before " + mode);
            output.Configure(mode == "disabled" ? new HapticSettings() : settings, mode != "background", mode != "attribution", mode != "layout", mode != "native");
            fixed (float* pcm = samples) output.Mix(stale, pcm, samples.Length, 0);
            check(output.RumbleSignal.TakePeak() == 0 && !output.RumbleSignal.Active, "Rumble survived " + mode);
        }
        output.Configure(new HapticSettings(FootstepAmplifier: true), true, true, true, true);
        output.SetFootstepContext(true, false);
        check(!output.FootstepsActive && !output.CurrentSettings.NeedsAudio, "Native footstep amplification was synthesized for regular rumble");
        output.Configure(settings, true, true); // Existing DS5 mode remains the default.
        check(!output.Prepare(null, true, 2, 0).HasOutput && !output.RumbleSignal.Active,
            "DualSense routing fell back to regular rumble without being selected");
    }

    private static void CheckStrengthTest(Action<bool, string> check)
    {
        var devices = new Devices(); devices.Connected[0] = true;
        using var session = new RumbleSession(devices, () => false);
        var settings = new HapticSettings(Enabled: true, ScreenEffects: true, RumbleStrength: 0.5f, RumblePulseMilliseconds: 120);
        var signal = new SfxLoudness(); signal.SetActive(true);
        session.Select(HapticOutputMode.XInput, 0, true, 0);
        session.RequestStrengthTest(0);
        signal.Observe(new float[] { 1 }); session.Tick(signal, settings, true, 0, 1);
        var weak = devices.Plugin[0];
        check(weak == RumbleMotors.FromImpact(0.1f) && devices.Screen[0] == default,
            "The weak strength test was masked by combat or screen rumble");
        session.Tick(signal, settings, true, 0.7, 1);
        check(devices.Plugin[0] == default && devices.Screen[0] == default, "The strength test lacked a quiet gap");
        session.Tick(signal, settings, true, 1, 1);
        var strong = devices.Plugin[0];
        check(strong == RumbleMotors.FromImpact(0.5f) && strong.Low > weak.Low * 4 && strong.High > weak.High * 4,
            "Weak/strong test commands did not have different amplitudes");
        session.Tick(signal, settings, false, 1.01, 1);
        session.Tick(signal, settings, true, 1.02, 0);
        check(devices.Plugin[0] == default && devices.Screen[0] == default,
            "A background strength test resumed or replayed after focus return");

        foreach (var bluetooth in new[] { false, true })
        {
            var channelSettings = settings with { BluetoothRumble = bluetooth, BluetoothMotorBalance = 0 };
            session.RequestStrengthTest(2, true);
            signal.Observe(new float[] { 1 }); session.Tick(signal, channelSettings, true, 2, 1, (0, 1));
            var command = RumbleMotors.FromLevel(0.5f).Low;
            check(devices.Plugin[0] == new RumbleMotors(command, 0) && devices.Screen[0] == default,
                "Left-only test was affected by combat panning, frequency weighting or screen rumble");
            session.Tick(signal, channelSettings, true, 2.7, 1);
            check(devices.Plugin[0] == default && devices.Screen[0] == default,
                "Left/right motor test did not leave a silent gap");
            session.Tick(signal, channelSettings, true, 3, 1);
            check(devices.Plugin[0] == new RumbleMotors(0, command) && devices.Screen[0] == default,
                "Right-only test did not use exactly the left test's strength");
            session.Tick(signal, channelSettings, true, 3.6);
            check(devices.Plugin[0] == default, "Motor test kept vibrating after its deadline");
            session.RequestStrengthTest(4, true);
            session.Tick(signal, channelSettings, true, 4);
            session.Tick(signal, channelSettings, false, 4.1);
            session.Tick(signal, channelSettings, true, 5);
            check(devices.Plugin[0] == default && devices.Screen[0] == default,
                "Left/right motor test resumed after losing focus");
        }
        var output = new NativeHapticOutput(null, 0, 0, 0);
        output.Configure(settings, true, true, true, true);
        var frame = output.Prepare(null, true, 2, 0);
        var samples = new float[2048]; Array.Fill(samples, 0.1f);
        output.Configure(settings with { RumbleStrength = 0.2f, RumblePulseMilliseconds = 250 }, true, true, true, true);
        fixed (float* pcm = samples) output.Mix(frame, pcm, samples.Length, 0);
        check(output.CurrentSettings.RumbleStrength == 0.2f && output.CurrentSettings.RumblePulseMilliseconds == 250 &&
            output.RumbleSignal.TakePeak() == 0, "Changing basic-rumble controls retained an old audio frame/settings snapshot");
    }
}
