// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static class Program
{
    private static int checks;
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    public static async Task Main(string[] args)
    {
        if (args.Contains("--probe")) { Probe(); return; }
        if (args.Contains("--benchmark")) { PerformanceChecks.Run(); return; }
        CheckReports();
        CheckEffects();
        CheckRgbAnimations();
        CheckSfxLoudness();
        CheckSfxColors();
        CheckSfxAudioLayout();
        CheckHaptics();
        HapticEnableCueChecks.Run(Assert);
        FootstepHapticChecks.Run(Assert);
        CheckSpatialHaptics();
        CheckImpactBias();
        CheckNativeStereoMatrices();
        CheckPlayerAnimations();
        LimitBreakChecks.Run(Assert);
        UiTextChecks.Run(Assert);
        PluginInfoChecks.Run(Assert);
        PluginCommandChecks.Run(Assert);
        CheckControllerPreview();
        await CheckOwnership();
        await CheckSolidRefresh();
        await CheckFocusRace();
        await CheckMultipleControllers();
        await CheckReconnectAndFailure();
        await CheckScanBackoff();
        await CheckWriteTimeout();
        await CheckCancellation();
        await CheckGlobalRelease();
        await CheckReleaseRegainFocus();
        await CheckReleaseFailure();
        await CheckAnimatedIndicatorSession();
        await CheckLimitBreakSession();
        await CheckAnimatedRgbSessions();
        await CheckSfxSession();
        Console.WriteLine($"Passed {checks:N0} controller and UI checks: native report preservation, haptic routing, background guard, effects, focus transitions, reconnect, cancellation and localization.");
    }

    private static async Task CheckLimitBreakSession()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground);
        devices.OnOpen = () => devices.Opened.Last().AllowBackground = true;
        using var session = new LightingSession(devices, () => foreground, _ => throw new Exception("Unexpected LB output failure"));
        var settings = LightingSettings.Default with { PlayerMode = PlayerIndicatorMode.LimitBreak };
        LimitBreakState State(double time, ushort units = 10000) => new(true, 1, units, 10000, time);
        await session.TickAsync(settings, 0.25, default, 0, State(0.25));
        var lit = devices.Opened.Last().Reports.Last();
        await session.TickAsync(settings, 1.25, default, 0, State(1.25));
        var dark = devices.Opened.Last().Reports.Last();
        Assert(lit[43] == 0 && dark[43] == 0 && lit[44] == (27 | 0x20) && dark[44] == 0x20,
            "Full-charge LB blink did not send immediate on/off frames at full brightness");
        foreground = false;
        await session.TickAsync(settings, 1.3, default, 0, State(1.3));
        await session.TickAsync(settings, 1.4, default, 0, State(1.4));
        Assert(session.ReleaseCount == 1 && devices.Opened.Last().Disposed && devices.Opened.Last().Reports.Last().Skip(43).Take(5).All(b => b == 0),
            "LB mode did not turn off once and release on focus loss");
        foreground = true;
        await session.TickAsync(settings, 2.25, default, 0, State(2.25));
        Assert((devices.Opened.Last().Reports.Last()[44] & 31) == 27 && devices.Opened.Last().Reports.Last()[43] == 0,
            "LB mode did not resume current charge after focus return");
        devices.Paths = []; await session.TickAsync(settings, 3.25, default, 0, State(3.25));
        devices.Paths = ["controller"]; await session.TickAsync(settings, 4.25, default, 0, State(4.25));
        Assert((devices.Opened.Last().Reports.Last()[44] & 31) == 27, "LB mode did not recover after reconnect");
        await session.TickAsync(settings with { PlayerMode = PlayerIndicatorMode.Animation, PlayerLights = 10 }, 4.35, default);
        Assert(devices.Opened.Last().Reports.Last()[44] == 10 && devices.Opened.Last().Reports.Last()[43] == 0,
            "Returning from LB mode did not restore manual LEDs/full brightness");
    }

    private static void CheckReports()
    {
        var frame = new LightingFrame(11, 222, 33, 0);
        foreach (var length in new[] { 48, 63, 64 })
        for (var mask = 0; mask <= 255; mask++)
        {
            var original = new byte[length];
            new Random(mask + length).NextBytes(original);
            original[0] = 2;
            original[2] = (byte)mask;
            original[39] = (byte)mask;
            var active = (byte[])original.Clone();
            Assert(DualSenseReport.Apply(active, true, true, frame) == ReportAction.CustomLighting, "Foreground report rejected");
            Assert(active[2] == (byte)((mask & ~8) | 0x14), "Incorrect foreground validity");
            Assert(active[39] == (byte)(mask | 3), "Improved-rumble flag changed");
            Assert(active[42] == 2 && active[43] == 0, "Pulse/brightness command missing");
            Assert(active[44] == 0 && active[45] == 11 && active[46] == 222 && active[47] == 33, "Incorrect color/player bytes");
            for (var i = 0; i < length; i++)
                if (i != 2 && i != 39 && i != 42 && i != 43 && (i < 44 || i > 47))
                    Assert(active[i] == original[i], $"Foreground modified native field {i}");

            foreach (var enabled in new[] { true, false })
            {
                var background = (byte[])original.Clone();
                Assert(DualSenseReport.Apply(background, false, enabled, frame) == ReportAction.BackgroundGuard, "Background guard missing");
                for (var i = 0; i < length; i++)
                {
                    var expected = i == 2 ? (byte)(original[i] & ~0x1c) : i == 39 ? (byte)(original[i] & ~3) : original[i];
                    Assert(background[i] == expected, $"Background modified native field {i}");
                }
            }
            var disabled = (byte[])original.Clone();
            Assert(DualSenseReport.Apply(disabled, true, false, frame) == ReportAction.Unchanged && disabled.SequenceEqual(original), "Disabled custom lighting changed native foreground output");
        }

        for (var length = 0; length < 260; length++)
        {
            if (DualSenseReport.IsSupportedLength(length)) continue;
            var bytes = Enumerable.Repeat((byte)2, length).ToArray();
            var copy = (byte[])bytes.Clone();
            Assert(DualSenseReport.Apply(bytes, true, true, frame) == ReportAction.Unchanged && bytes.SequenceEqual(copy), "Unsupported report modified");
        }
        foreach (var reportId in new byte[] { 0, 1, 3, 0x31 })
        {
            var bytes = new byte[63]; bytes[0] = reportId;
            Assert(DualSenseReport.Apply(bytes, false, true, frame) == ReportAction.Unchanged && bytes[0] == reportId, "Foreign report modified");
        }
        foreach (var length in new[] { 48, 63, 64 })
        {
            var report = DualSenseReport.Create(length, frame);
            Assert(report[0] == 2 && report[1] == 0 && report[2] == 0x14 && report[39] == 3, "LED-only report enabled a non-lighting function");
            Assert(report.Skip(3).Take(36).All(b => b == 0), "LED-only output contains haptic/audio/trigger data");
        }
    }

    private static void CheckEffects()
    {
        var rainbow = LightingSettings.Default with { Effect = LightingEffect.Rainbow, CycleSeconds = 6 };
        Assert(LightingEffects.Render(rainbow, 0) == new LightingFrame(255, 0, 0, 0), "Rainbow red");
        Assert(LightingEffects.Render(rainbow, 2) == new LightingFrame(0, 255, 0, 0), "Rainbow green");
        Assert(LightingEffects.Render(rainbow, 4) == new LightingFrame(0, 0, 255, 0), "Rainbow blue");
        Assert(LightingEffects.Render(rainbow, 6) == LightingEffects.Render(rainbow, 0), "Rainbow not periodic");
        var breathing = LightingSettings.Default with { Effect = LightingEffect.Breathing, CycleSeconds = 6 };
        Assert(LightingEffects.Render(breathing, 0) == new LightingFrame(119, 255, 0, 0), "Breathing peak");
        Assert(LightingEffects.Render(breathing, 3) == new LightingFrame(0, 0, 0, 0), "Breathing trough");
        Assert(LightingEffects.Render(rainbow with { Brightness = 0, PlayerLights = 4 }, 2) == new LightingFrame(0, 0, 0, 4), "Brightness/player independence");
        var invalid = (rainbow with { Brightness = float.NaN, CycleSeconds = 0, Effect = (LightingEffect)99, PlayerLights = 255 }).Normalize();
        Assert(invalid.Brightness == 1 && invalid.CycleSeconds == 1 && invalid.Effect == LightingEffect.Solid && invalid.PlayerLights == 31, "Invalid settings not normalized");
        for (var i = 0; i < 1000; i++)
        {
            var a = LightingEffects.Render(rainbow, i / 100.0);
            var b = LightingEffects.Render(rainbow, i / 100.0 + 0.001);
            Assert(Math.Abs(a.Red - b.Red) <= 1 && Math.Abs(a.Green - b.Green) <= 1 && Math.Abs(a.Blue - b.Blue) <= 1, "Rainbow discontinuity");
        }
    }

    private static void CheckRgbAnimations()
    {
        Assert((int)LightingEffect.Solid == 0 && (int)LightingEffect.Rainbow == 1 && (int)LightingEffect.Breathing == 2,
            "Existing saved RGB effect selections changed meaning");
        var settings = LightingSettings.Default with
        {
            Red = 255, Green = 0, Blue = 0, SecondaryRed = 0, SecondaryGreen = 0, SecondaryBlue = 255, CycleSeconds = 8,
        };
        LightingFrame At(LightingEffect effect, double phase) => LightingEffects.Render(settings with { Effect = effect }, phase * 8);
        int Peak(LightingFrame frame) => Math.Max(frame.Red, Math.Max(frame.Green, frame.Blue));
        bool Near(LightingFrame a, LightingFrame b) => Math.Abs(a.Red - b.Red) <= 1 &&
            Math.Abs(a.Green - b.Green) <= 1 && Math.Abs(a.Blue - b.Blue) <= 1;

        Assert(At(LightingEffect.ColorShift, 0) == new LightingFrame(255, 0, 0, 0) &&
            At(LightingEffect.ColorShift, 0.5) == new LightingFrame(0, 0, 255, 0) &&
            At(LightingEffect.ColorShift, 0.25) == new LightingFrame(128, 0, 128, 0), "Color Shift missed its chosen colors or midpoint");
        var sameColor = settings with { Effect = LightingEffect.ColorShift, SecondaryRed = 255, SecondaryGreen = 0, SecondaryBlue = 0 };
        Assert(LightingEffects.Render(sameColor, 1.37) == new LightingFrame(255, 0, 0, 0), "Identical Color Shift endpoints should stay steady");
        Assert(At(LightingEffect.Heartbeat, 0.12).Red == 255 && At(LightingEffect.Heartbeat, 0.39).Red is >= 177 and <= 179 &&
            At(LightingEffect.Heartbeat, 0.23).Red == 0 && At(LightingEffect.Heartbeat, 0.8).Red == 0,
            "RGB Heartbeat lost its two distinct pulses and rest");
        Assert(At(LightingEffect.Comet, 0).Red == 0 && At(LightingEffect.Comet, 0.06).Red == 255 &&
            At(LightingEffect.Comet, 0.4).Red > At(LightingEffect.Comet, 0.8).Red && At(LightingEffect.Comet, 1).Red == 0,
            "Comet lost its fast flare or fading tail");
        Assert(At(LightingEffect.Aurora, 0).Green > At(LightingEffect.Aurora, 0).Red &&
            At(LightingEffect.Aurora, 0.5).Blue > At(LightingEffect.Aurora, 0.5).Green, "Aurora does not move from teal into violet");
        Assert(Peak(At(LightingEffect.RainbowPulse, 0)) == 255 && Peak(At(LightingEffect.RainbowPulse, 1.0 / 6)) < 40,
            "Rainbow Pulse does not breathe while its hue changes");

        foreach (var effect in Enum.GetValues<LightingEffect>().Where(e => !LightingEffects.IsSfx(e)))
        {
            var s = settings with { Effect = effect, PlayerLights = 21, PlayerAnimation = PlayerIndicatorAnimation.Chase };
            var colors = new HashSet<(byte, byte, byte)>();
            foreach (var cycle in new[] { 1f, 3.7f, 60f })
            for (var i = 0; i < 120; i++)
            {
                s = s with { CycleSeconds = cycle };
                var time = i * cycle / 120.0;
                var frame = LightingEffects.Render(s, time);
                colors.Add((frame.Red, frame.Green, frame.Blue));
                Assert(Near(frame, LightingEffects.Render(s, time + cycle)), $"RGB loop did not repeat: {effect}");
                Assert(ControllerPreviewGeometry.Frame(s, true, time) == frame, $"RGB preview differs from output: {effect}");
                var dim = LightingEffects.Render(s with { Brightness = 0.25f }, time);
                Assert(Math.Abs(dim.Red - frame.Red * 0.25) <= 0.5 && Math.Abs(dim.Green - frame.Green * 0.25) <= 0.5 &&
                    Math.Abs(dim.Blue - frame.Blue * 0.25) <= 0.5 && Peak(dim) <= 64, $"RGB brightness ceiling ignored: {effect}");
                var off = LightingEffects.Render(s with { Brightness = 0 }, time);
                var indicators = PlayerIndicatorEffects.Render(s.PlayerLights, s.PlayerAnimation, s.PlayerCycleSeconds, time);
                Assert(Peak(off) == 0 && off.PlayerLights == indicators && frame.PlayerLights == indicators && frame.PlayerLightsImmediate,
                    $"RGB effect changed white indicator timing or brightness: {effect}");
                if (effect == LightingEffect.Fire)
                    Assert(frame.Red >= frame.Green && frame.Green > frame.Blue && frame.Red >= 110, "Fire left its warm palette or became a strobe");
            }
            Assert(effect == LightingEffect.Solid ? colors.Count == 1 : colors.Count > 10, $"RGB effect appears frozen: {effect}");
            var start = LightingEffects.Render(s, 0);
            Assert(Near(start, LightingEffects.Render(s, s.CycleSeconds - 0.000001)), $"RGB loop has a visible seam: {effect}");
            foreach (var time in new[] { -1.0, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
                Assert(LightingEffects.Render(s, time) == start, $"RGB effect mishandled invalid time: {effect}");
            for (var i = 0; i < 100; i++)
            {
                var time = i * 8 / 100.0;
                Assert(Near(LightingEffects.Render(s with { CycleSeconds = 8 }, time),
                    LightingEffects.Render(s with { CycleSeconds = 8 }, time + 0.0001)), $"RGB animation jumps between adjacent times: {effect}");
            }
        }
    }

    private static async Task CheckAnimatedRgbSessions()
    {
        foreach (var effect in Enum.GetValues<LightingEffect>().Where(e => e != LightingEffect.Solid && !LightingEffects.IsSfx(e)))
        {
            var foreground = true;
            var devices = new FakeDevices(() => foreground);
            devices.OnOpen = () => devices.Opened.Last().AllowBackground = true;
            using var session = new LightingSession(devices, () => foreground, _ => throw new Exception($"Unexpected {effect} failure"));
            var settings = LightingSettings.Default with { Effect = effect, CycleSeconds = 2, PlayerLights = 10 };
            void Matches(double time)
            {
                var frame = LightingEffects.Render(settings, time);
                var report = devices.Opened.Last().Reports.Last();
                Assert(report[44] == 10 && report[45] == frame.Red && report[46] == frame.Green && report[47] == frame.Blue,
                    $"RGB session differs from effect rendering: {effect}");
                Assert(report[1] == 0 && report[2] == 0x14 && report.Skip(3).Take(36).All(b => b == 0),
                    $"RGB animation enabled native haptic/audio/trigger changes: {effect}");
            }
            await session.TickAsync(settings, 0, default);
            await session.TickAsync(settings, 0.24, default);
            Matches(0.24);
            Assert(session.WriteCount == 2, $"RGB effect did not animate without indicator animation: {effect}");
            foreground = false;
            await session.TickAsync(settings, 0.5, default);
            Assert(session.ReleaseCount == 1 && session.DeviceCount == 0 && devices.Opened.Last().Disposed &&
                devices.Opened.Last().Reports.Last().Skip(44).Take(4).All(b => b == 0), $"RGB effect did not turn off and release on focus loss: {effect}");
            await session.TickAsync(settings, 1.5, default);
            Assert(session.WriteCount == 2 && session.ReleaseCount == 1, $"RGB effect continued writing in background: {effect}");
            foreground = true;
            await session.TickAsync(settings, 2, default);
            Matches(2);
            devices.Paths = [];
            await session.TickAsync(settings, 3, default);
            devices.Paths = ["controller"];
            await session.TickAsync(settings, 4.24, default);
            Matches(4.24);
            Assert(session.WriteCount == 4, $"RGB animation did not return after focus change and reconnect: {effect}");
        }
    }

    private static void CheckSfxLoudness()
    {
        var meter = new SfxLoudness();
        float[] samples = [0.2f, -0.2f, 0.2f, -0.2f];
        var original = (float[])samples.Clone();
        meter.Observe(samples);
        Assert(meter.TakePeak() == 0, "SFX was recorded before opting in");
        meter.SetActive(true);
        meter.Observe(samples);
        Assert(Math.Abs(meter.TakePeak() - 0.2f) < 0.00001 && samples.SequenceEqual(original), "SFX RMS is incorrect or modified playback samples");
        Assert(meter.TakePeak() == 0, "SFX observation was replayed twice");
        meter.Observe([0.4f, -0.4f]);
        meter.Observe([0.1f, -0.1f]);
        meter.Observe([0, 0]);
        Assert(Math.Abs(meter.TakePeak() - 0.4f) < 0.00001, "Quiet concurrent voices diluted an impact");
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1000f })
        {
            meter.Observe([0.2f, invalid]);
            Assert(meter.TakePeak() == 0, "Invalid native sample caused a flash");
        }
        meter.Observe([2, -2]);
        Assert(meter.TakePeak() == 1, "Loud audio did not saturate safely");
        var oldGeneration = meter.Generation;
        meter.Observe(samples);
        meter.SetActive(false);
        Assert(meter.TakePeak() == 0, "Focus loss retained pending SFX");
        meter.SetActive(true);
        meter.Observe(samples, oldGeneration);
        Assert(meter.TakePeak() == 0, "A callback spanning Alt-Tab republished stale audio after focus return");
        meter.Observe(samples);
        Assert(meter.TakePeak() > 0, "New SFX did not recover after focus return");

        // A warmed audio callback must not allocate; buffers already belong to the game.
        for (var i = 0; i < 20; i++) { meter.Observe(samples); meter.TakePeak(); }
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) { meter.Observe(samples); meter.TakePeak(); }
        Assert(GC.GetAllocatedBytesForCurrentThread() == allocated, "SFX callback allocated on the audio thread");

        var left = new SfxEnvelope(); var right = new SfxEnvelope();
        var timingSettings = LightingSettings.Default;
        left.Update(1, 0, timingSettings, true); right.Update(1, 0, timingSettings, true);
        for (var i = 1; i < 60; i++) right.Update(0, i / 60.0, timingSettings, true);
        Assert(Math.Abs(left.Update(0, 1, timingSettings, true) - right.Update(0, 1, timingSettings, true)) < 0.00001,
            "SFX decay depends on UI frame rate or polling frequency");

        var settings = LightingSettings.Default with { Effect = LightingEffect.SfxLoudness };
        var envelope = new SfxEnvelope();
        Assert(envelope.Update(0.001f, 0, settings, true) == 0, "SFX noise gate did not suppress quiet noise");
        var peak = envelope.Update(0.052f, 0.033, settings, true);
        Assert(Math.Abs(peak - 0.4f) < 0.00001, "SFX sensitivity did not scale the measured loudness");
        var faded = envelope.Update(0, 0.213, settings, true);
        Assert(Math.Abs(faded - peak / Math.E) < 0.00001, "SFX release time does not follow its configured decay");
        Assert(envelope.Update(0, 0.213, settings, true) == faded, "Reading the same moment advanced the envelope");
        Assert(envelope.Update(0, 10, settings, true) == 0, "A stalled audio source left a stale light on");
        Assert(envelope.Update(1, 11, settings, true) == 1 && envelope.Update(1, 11.1, settings, false) == 0,
            "SFX envelope did not reset immediately when disabled or backgrounded");
        Assert(envelope.Update(0, 12, settings, true) == 0, "Old SFX returned when resuming from background");
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0 })
            Assert(envelope.Update(1, invalid, settings, true) == 0, "Invalid audio clock caused a flash");
        envelope.Update(1, 20, settings, true);
        Assert(envelope.Update(1, 19, settings, true) == 0, "Clock reset retained an old envelope");
        var normalized = (settings with { SfxSensitivity = float.NaN, SfxNoiseGate = -1, SfxReleaseSeconds = float.PositiveInfinity,
            AudioSource = (SfxSource)99 }).Normalize();
        Assert(normalized.SfxSensitivity == 8 && normalized.SfxNoiseGate == 0 && normalized.SfxReleaseSeconds == 0.18f &&
            normalized.AudioSource == SfxSource.PlayerOnly && LightingSettings.Default.AudioSource == SfxSource.PlayerOnly,
            "Invalid SFX controls were not normalized");
        foreach (var sensitivity in new[] { 1f, 8f, 32f })
        {
            var automatic = settings with { SfxSensitivity = sensitivity, SfxNoiseGate = 0.05f };
            var autoEnvelope = new SfxEnvelope();
            Assert(autoEnvelope.Update(0.008f / sensitivity, 0, automatic, true) == 0,
                "Automatic gate did not suppress the same faint light level across sensitivities");
            Assert(Math.Abs(autoEnvelope.Update(0.216f / sensitivity, 0.033, automatic, true) - 0.2f) < 0.00001,
                "Automatic gate changed a skill's scaled response or used a hidden manual threshold");
        }
        Assert(new SfxEnvelope().Update(0.01f, 0, settings with { SfxAutomaticGate = false, SfxNoiseGate = 0.05f }, true) == 0 &&
            new SfxEnvelope().Update(0.01f, 0, settings with { SfxAutomaticGate = false, SfxNoiseGate = 0 }, true) > 0,
            "Advanced manual noise-gate override was ignored");

        foreach (var mode in new[] { LightingEffect.SfxLoudness, LightingEffect.SfxColorBlend })
        foreach (var animation in Enum.GetValues<PlayerIndicatorAnimation>())
        foreach (var level in new[] { -1f, 0, 0.25f, 0.8f, 1, 5, float.NaN, float.PositiveInfinity })
        {
            var s = settings with { Effect = mode, Red = 255, Green = 128, Blue = 64, SecondaryRed = 255, SecondaryGreen = 128, SecondaryBlue = 64,
                Brightness = 0.5f, PlayerLights = 27, PlayerAnimation = animation };
            var frame = LightingEffects.Render(s, 0.37, level);
            Assert(ControllerPreviewGeometry.Frame(s, true, 0.37, level) == frame, "SFX preview and hardware use different loudness");
            Assert(ControllerPreviewGeometry.Frame(s, false, 0.37, level) == default, "SFX preview remained on in background");
            Assert(frame.PlayerLights == PlayerIndicatorEffects.Render(s.PlayerLights, animation, s.PlayerCycleSeconds, 0.37),
                "SFX loudness changed an independent white indicator animation");
            Assert(frame.Red <= 128 && frame.Green <= 64 && frame.Blue <= 32, "SFX ignored the brightness ceiling");
            var later = LightingEffects.Render(s, 3.14, level);
            Assert(later.Red == frame.Red && later.Green == frame.Green && later.Blue == frame.Blue,
                "SFX RGB consumers drifted by calculating separate time envelopes");
            if (!float.IsFinite(level) || level <= 0)
                Assert(mode == LightingEffect.SfxLoudness ? frame.Red == 0 && frame.Green == 0 && frame.Blue == 0 :
                    frame.Red == 128 && frame.Green == 64 && frame.Blue == 32, "Quiet-state behavior is incorrect for the selected SFX mode");
            foreach (var length in new[] { 48, 63, 64 })
            {
                var report = Enumerable.Range(0, length).Select(i => (byte)(i * 13)).ToArray(); report[0] = 2;
                var originalReport = (byte[])report.Clone();
                DualSenseReport.Apply(report, true, true, frame);
                for (var i = 0; i < length; i++)
                    if (i != 2 && i != 39 && i != 42 && i != 43 && (i < 44 || i > 47))
                        Assert(report[i] == originalReport[i], "SFX lighting changed native haptic, audio or trigger bytes");
            }
        }
    }

    private static void CheckSfxColors()
    {
        Assert((int)LightingEffect.SfxLoudness == 9 && (int)LightingEffect.SfxColorBlend == 10 &&
            LightingEffects.IsSfx(LightingEffect.SfxLoudness) && LightingEffects.IsSfx(LightingEffect.SfxColorBlend),
            "Original SFX selection changed identity or either mode is excluded from audio capture");
        var settings = LightingSettings.Default with
        {
            Effect = LightingEffect.SfxColorBlend, Red = 0, Green = 0, Blue = 255,
            SecondaryRed = 255, SecondaryGreen = 0, SecondaryBlue = 0, PlayerLights = 10,
        };
        Assert(LightingEffects.Render(settings, 0, 0.01f) == new LightingFrame(3, 0, 252, 10),
            "A quiet SFX dimmed the low color to invisibility");
        Assert(LightingEffects.Render(settings, 0, 0.5f) == new LightingFrame(128, 0, 128, 10),
            "Half-loud SFX did not blend both colors at the configured brightness");
        Assert(LightingEffects.Render(settings, 0, 1) == new LightingFrame(255, 0, 0, 10),
            "Maximum SFX did not reach the high color");
        Assert(LightingEffects.Render(settings, 0, 0) == new LightingFrame(0, 0, 255, 10),
            "The low SFX color disappeared between sounds");
        Assert(LightingEffects.Render(settings with { Brightness = 0.25f }, 0, 1) == new LightingFrame(64, 0, 0, 10),
            "The high SFX color ignored the overall brightness limit");
        var swapped = settings with { Red = 255, Blue = 0, SecondaryRed = 0, SecondaryBlue = 255 };
        Assert(LightingEffects.Render(swapped, 0, 0.01f) == new LightingFrame(252, 0, 3, 10) &&
            LightingEffects.Render(swapped, 0, 1) == new LightingFrame(0, 0, 255, 10), "Changing endpoint colors did not update the SFX gradient");
        var original = settings with { Effect = LightingEffect.SfxLoudness };
        Assert(LightingEffects.Render(original, 0, 0) == new LightingFrame(0, 0, 0, 10) &&
            LightingEffects.Render(original, 0, 0.5f) == new LightingFrame(0, 0, 128, 10) &&
            LightingEffects.Render(original, 0, 1) == new LightingFrame(0, 0, 255, 10),
            "Original SFX Loudness no longer pulses only the chosen single color");
        var gatedLevel = new SfxEnvelope().Update(0.001f, 0, settings, true);
        Assert(gatedLevel == 0 && LightingEffects.Render(settings, 0, gatedLevel) == new LightingFrame(0, 0, 255, 10),
            "The automatic gate made the two-color quiet state go dark");
        Assert(LightingEffects.Render(settings with { Brightness = 0.25f }, 0, 0) == new LightingFrame(0, 0, 64, 10) &&
            LightingEffects.Render(settings with { Brightness = 0 }, 0, 0) == new LightingFrame(0, 0, 0, 10),
            "The low SFX color ignored the user's brightness setting");
        var previous = LightingEffects.Render(settings, 0, 0);
        for (var i = 1; i <= 100; i++)
        {
            var level = i / 100f;
            var frame = LightingEffects.Render(settings, 1.25, level);
            Assert(ControllerPreviewGeometry.Frame(settings, true, 1.25, level) == frame, "Two-color SFX preview diverged from hardware");
            Assert(frame.Red >= previous.Red && frame.Blue <= previous.Blue &&
                Math.Abs(frame.Red - previous.Red) <= 3 && Math.Abs(frame.Blue - previous.Blue) <= 3,
                "The SFX color blend jumped between nearby loudness levels");
            Assert(ControllerPreviewGeometry.Frame(settings, false, 1.25, level) == default &&
                ControllerPreviewGeometry.Frame(settings with { Enabled = false }, true, 1.25, level) == default,
                "The SFX gradient remained visible in background or while disabled");
            previous = frame;
        }
    }

    private static unsafe void CheckSfxAudioLayout()
    {
        for (byte bus = 0; bus <= 20; bus++)
        for (uint flags = 0; flags < 128; flags++)
            Assert(GameAudioLayout.IsCombatSound(bus, flags) == ((bus == 2 || bus == 10) && (flags & 4) == 0),
                "SFX filter included music, voices, ambience, footsteps or vibration-only audio");
        Assert(GameAudioLayout.SampleCount(256, 8, 0, 0) == 2048 && GameAudioLayout.SampleCount(256, 8, 1, 1) == 4096,
            "Native audio sample span has an incorrect size");
        Assert(GameAudioLayout.SampleCount(512, 8, 0, 0) == 0 && GameAudioLayout.SampleCount(256, 2, 0, 0) == 0 &&
            GameAudioLayout.SampleCount(256, 8, 2, 0) == 0 && GameAudioLayout.SampleCount(256, 8, 0, 2) == 0,
            "An unsupported mixer layout was accepted");
        GameAudioLayout.SourceVoice source = default;
        GameAudioLayout.VoiceOwner owner = default;
        GameAudioLayout.Track track = default;
        GameAudioLayout.RootSound sound = default;
        const int vtable = 0x112233;
        source.State = 3; source.QueuedBufferCount = 1; source.SendCount = 1; source.Pitch = 1; source.Volume = 1;
        source.Callback = (nint)(&owner.CallbackVTable);
        owner.CallbackVTable = vtable; owner.Source = &source; owner.Track = &track;
        track.Voice = &owner; track.Owner = &sound; sound.BusId = 2;
        Assert(GameAudioLayout.IsCombatVoice(&source, vtable), "A combat voice was rejected");
        var pool = stackalloc GameAudioLayout.SoundData[GameAudioLayout.PlayerSoundOwnership.Capacity];
        new Span<GameAudioLayout.SoundData>(pool, GameAudioLayout.PlayerSoundOwnership.Capacity).Clear();
        var ownership = new GameAudioLayout.PlayerSoundOwnership(pool);
        ownership.SetPlayer(0x1110, 100, 0x2220);
        var player = ownership.Player;
        Assert(player.Owns(0x1110, 0) && player.Owns(0, 0x2220) &&
            !player.Owns(0, 0) && !player.Owns(0x3330, 0x2220), "A listener's target/draw pointer was mistaken for its caster");
        sound.SoundId = 123;
        pool[3].IsActive = 1; pool[3].SoundId = 123;
        sound.ExtendedFlags = 0x10; sound.PlayerCategory = 0;
        Assert(!GameAudioLayout.IsCombatVoice(&source, vtable, SfxSource.PlayerOnly, ownership) &&
            GameAudioLayout.IsCombatVoice(&source, vtable), "A teammate's category-zero hit leaked into player-only capture");
        ownership.Acquired(pool + 3, player);
        sound.ExtendedFlags = 0; sound.PlayerCategory = 3;
        Assert(GameAudioLayout.IsCombatVoice(&source, vtable, SfxSource.PlayerOnly, ownership),
            "A confirmed local caster was lost because of its volume category/flags");
        Assert(!ownership.SetPlayer(0x1110, 100, 0x2220, new nint[] { 0x2220, 0x4440, 0x5550 }) &&
            ReferenceEquals(ownership.Player, player) && player.Owns(0, 0x4440) && player.Owns(0, 0x5550),
            "A local weapon/attachment model could not identify its actor");
        Assert(!player.Owns(0x3330, 0x4440), "Another caster inherited ownership through our model");
        ownership.SetPlayer(0x1110, 100, 0x2220, new nint[] { 0x2220, 0x5550 });
        Assert(!player.Owns(0, 0x4440) && ownership.Contains(123),
            "A weapon-model refresh either retained a removed model or discarded an already attributed sound");
        ownership.Acquired(pool + 3, null);
        Assert(!ownership.Contains(123), "A pooled sound reservation inherited the previous player's ownership");
        pool[3].SoundId = -1;
        ownership.Acquired(pool + 3, player);
        Assert(!ownership.Contains(-1) && !ownership.Contains(123), "An unloaded sound matched stale PCM");
        pool[3].SoundId = 456; sound.SoundId = 456;
        Assert(GameAudioLayout.IsCombatVoice(&source, vtable, SfxSource.PlayerOnly, ownership),
            "An asynchronously loaded player sound never became eligible");
        pool[3].IsActive = 0;
        Assert(!ownership.Contains(456), "A released sound triggered player-only RGB");
        pool[3].IsActive = 1;
        ownership.SetPlayer(0, 0, 0);
        Assert(!ownership.Contains(456), "Logout retained the old player's active sounds");
        ownership.SetPlayer(0x1110, 101, 0x2220);
        ownership.Acquired(pool + 3, player);
        Assert(!ownership.Contains(456), "An old callback attributed sounds to a newly logged-in actor at the same address");
        ownership.Acquired(pool + 3, ownership.Player);
        // Native positional playback uses a negative layout handle at SoundData+0x40
        // and a separate positive mixer ID in the layout entry's embedded controller.
        pool[3].SoundId = unchecked((int)0x804C3998);
        sound.SoundId = 492241941;
        ownership.LinkPlayback(pool[3].SoundId, sound.SoundId);
        Assert(GameAudioLayout.IsCombatVoice(&source, vtable, SfxSource.PlayerOnly, ownership),
            "A positional player's logical handle did not match its audible playback ID");
        Assert(!ownership.Contains(sound.SoundId + 1), "Another playback was accepted for the same positional asset");
        ownership.Acquired(pool + 3, null);
        ownership.LinkPlayback(pool[3].SoundId, sound.SoundId);
        Assert(!ownership.Contains(sound.SoundId), "A positional playback link survived pool reuse by another actor");
        ownership.Acquired(pool + 3, ownership.Player);
        pool[3].SoundId++;
        Assert(!ownership.Contains(sound.SoundId), "A new reservation inherited an old logical-to-playback mapping");
        ownership.LinkPlayback(pool[3].SoundId, sound.SoundId);
        ownership.SetPlayer(0, 0, 0);
        Assert(!ownership.Contains(sound.SoundId), "A positional playback link survived logout");
        ownership.SetPlayer(0x1110, 101, 0x2220);
        ownership.Acquired(pool + 3, ownership.Player);
        pool[3].SoundId = 456; sound.SoundId = 456;
        sound.ExtendedFlags = 0x14;
        Assert(!GameAudioLayout.IsCombatVoice(&source, vtable, SfxSource.PlayerOnly, ownership), "A player's vibration-only sound triggered RGB");
        // Neither pool observations nor player classification may allocate on audio threads.
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            ownership.Acquired(pool + 3, ownership.Player);
            ownership.Contains(456);
        }
        Assert(GC.GetAllocatedBytesForCurrentThread() == allocated, "SFX source classification allocated on the audio thread");
        sound.ExtendedFlags = 0;
        source.Volume = 0;
        Assert(!GameAudioLayout.IsCombatVoice(&source, vtable), "A muted voice exposed stale mixer scratch samples");
        source.PreviousVolume = 1;
        Assert(GameAudioLayout.IsCombatVoice(&source, vtable), "A real fade-out block was incorrectly discarded");
        source.Volume = 1; source.PreviousVolume = 0;
        sound.BusId = 10; sound.ExtendedFlags = 2;
        Assert(GameAudioLayout.IsCombatVoice(&source, vtable), "Audible combat audio with a native haptic send was rejected");
        sound.ExtendedFlags = 4;
        Assert(!GameAudioLayout.IsCombatVoice(&source, vtable), "Vibration-only audio triggered RGB");
        sound.ExtendedFlags = 0;
        Assert(!GameAudioLayout.IsCombatVoice(&source, vtable + 1), "An unknown callback type was interpreted as a game voice");
        track.Voice = null;
        Assert(!GameAudioLayout.IsCombatVoice(&source, vtable), "Broken voice ownership was accepted");
        track.Voice = &owner; owner.Source = null;
        Assert(!GameAudioLayout.IsCombatVoice(&source, vtable), "A reused voice owner was accepted");
        owner.Source = &source; source.State = 0;
        Assert(!GameAudioLayout.IsCombatVoice(&source, vtable), "An inactive voice triggered RGB");
    }

    private static int hapticMixCalls;
    private static nint hapticDestination;
    private static float hapticGain;
    private static uint hapticFrames, hapticFormat;
    private static float hapticLeftSample, hapticRightSample;

    [UnmanagedCallersOnly]
    private static unsafe void CaptureHapticMix(nint destination, float* samples, uint frames, float gain, uint format)
    {
        hapticMixCalls++;
        hapticDestination = destination; hapticGain = gain;
        hapticFrames = frames; hapticFormat = format;
        hapticLeftSample = samples[0]; hapticRightSample = samples[1];
    }

    private static unsafe void CheckHaptics()
    {
        var enabled = new HapticSettings(true);
        Assert(!new HapticSettings().Enabled && enabled.Strength == 0.25f, "Added haptics must be opt-in at a moderate strength");
        Assert((enabled with { Strength = float.NaN }).Normalize().Strength == 0.25f &&
            (enabled with { Strength = 2 }).Normalize().Strength == 1 &&
            (enabled with { Strength = -1 }).Normalize().Strength == 0, "Haptic strength normalization failed");
        Assert(enabled.AdditionalGain(true, 2, 0, false) == 0.25f &&
            enabled.AdditionalGain(true, 10, 0, false) == 0.25f, "Own combat/weapon sounds cannot reach haptics");
        foreach (var bus in new byte[] { 0, 1, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13 })
            Assert(enabled.AdditionalGain(true, bus, 0, false) == 0, "Unrelated sound bus added haptics");
        Assert(enabled.AdditionalGain(false, 2, 0, false) == 0 &&
            enabled.AdditionalGain(true, 2, 0, true) == 0 &&
            new HapticSettings().AdditionalGain(true, 2, 0, false) == 0,
            "Foreign, disabled or already-routed sounds added haptics");
        foreach (var flag in new uint[] { 2, 4, 8, 6, 10, 12, 14 })
            Assert(enabled.AdditionalGain(true, 2, flag, false) == 0, "Native vibration was doubled");

        // A fake native graph exercises the real output path without touching a controller.
        var table = stackalloc nint[16];
        new Span<nint>(table, 16).Clear();
        var mix = (nint)(delegate* unmanaged<nint, float*, uint, float, uint, void>)&CaptureHapticMix;
        table[15] = mix;
        var destination = stackalloc byte[0x160];
        var replacement = stackalloc byte[0x160];
        new Span<byte>(destination, 0x160).Clear(); new Span<byte>(replacement, 0x160).Clear();
        *(nint*)destination = *(nint*)replacement = (nint)table;
        *(int*)(destination + 0xE0) = *(int*)(replacement + 0xE0) = 1;
        "Vibration\0"u8.CopyTo(new Span<byte>(destination + 0x13E, 10));
        "Vibration\0"u8.CopyTo(new Span<byte>(replacement + 0x13E, 10));
        var graph = stackalloc nint[15];
        new Span<nint>(graph, 15).Clear(); graph[9] = (nint)destination;
        var output = new NativeHapticOutput(graph, mix, (nint)table, (nint)table + 16 * 8);
        GameAudioLayout.SourceVoice voice = default; voice.SendCount = 1;
        var sends = (GameAudioLayout.VoiceSend*)((byte*)&voice + 0x940);
        sends[0].Destination = 0x123456; sends[0].Gain = 0.7f;
        var beforeVoice = new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice)).ToArray();
        var pcm = new float[4096]; Array.Fill(pcm, 0.1f);
        var beforePcm = (float[])pcm.Clone();
        hapticMixCalls = 0;
        Assert(!output.Active && output.Prepare(&voice, true, 2, 0).Destination == 0, "Haptics started before opt-in");
        output.Configure(enabled, true, true);
        var frame = output.Prepare(&voice, true, 2, 0);
        fixed (float* samples = pcm)
        {
            output.Mix(frame, samples, 2048, 0);
            Assert(hapticMixCalls == 1 && hapticDestination == (nint)destination && hapticGain == 0.25f &&
                hapticFrames == 256 && hapticFormat == 0 && output.Available, "Native vibration mixer arguments changed");
            output.Configure(enabled, false, true);
            output.Mix(frame, samples, 2048, 0);
            output.Configure(enabled, true, true);
            output.Mix(frame, samples, 2048, 0);
            Assert(hapticMixCalls == 1, "An in-flight block survived focus loss and return");
            output.Configure(enabled, true, false);
            Assert(!output.Active, "Haptics ran without player attribution");
            output.Configure(enabled, true, true);
            Assert(output.Prepare(&voice, false, 2, 0).Destination == 0, "A teammate's sound reached the mixer");
            sends[0].Destination = (nint)destination;
            Assert(output.Prepare(&voice, true, 2, 0).Destination == 0 && output.PreservedNativeBlocks == 1,
                "An existing vibration send was doubled");
            sends[0].Destination = 0x123456;
            Assert(output.Prepare(&voice, true, 2, 8).Destination == 0 && output.PreservedNativeBlocks == 2,
                "A native vibration flag was doubled");
            frame = output.Prepare(&voice, true, 10, 0);
            pcm[10] = float.NaN;
            output.Mix(frame, samples, 4096, 1);
            Assert(hapticMixCalls == 1, "Nonfinite PCM entered vibration processing");
            pcm[10] = 0.1f;
            output.Mix(frame, samples, 4096, 1);
            Assert(hapticMixCalls == 2 && hapticFormat == 1, "Ambisonic samples lost their native format");
            // The next block must follow a newly created graph, rather than a stale device pointer.
            graph[9] = (nint)replacement;
            output.Mix(output.Prepare(&voice, true, 2, 0), samples, 2048, 0);
            Assert(hapticMixCalls == 3 && hapticDestination == (nint)replacement, "Haptic output kept a stale graph after reconnect");
            graph[9] = 0;
            Assert(output.Prepare(&voice, true, 2, 0).Destination == 0 && !output.Available,
                "Missing vibration processing fell back to another audio output");
            graph[9] = (nint)replacement; table[15] = 0;
            Assert(output.Prepare(&voice, true, 2, 0).Destination == 0, "Unvalidated mixer method was accepted");
            table[15] = mix;
            var spatialSettings = enabled with { Spatial = true, DistanceFalloff = false };
            var bed = new float[16]; bed[0] = bed[9] = 1;
            var ambisonic = new float[32]; ambisonic[0] = ambisonic[17] = 1;
            output.SetSpatialMatrices(bed, ambisonic);
            output.Configure(spatialSettings, true, true);
            output.SetListener(new HapticListenerPose(new Vector3(100, 5, 200), Vector3.UnitX, Stopwatch.GetTimestamp()));
            var spatialFrame = output.Prepare(&voice, true, 2, 0, new Vector3(110, 5, 200));
            output.Mix(spatialFrame, samples, 2048, 0);
            Assert(hapticMixCalls == 4 && hapticLeftSample == 0 && MathF.Abs(hapticRightSample - 0.1f) < 0.00001f &&
                output.SpatialBlocks == 1 && output.LastSpatial.Pan == 1 && output.LastSpatial.Distance == 10,
                "Character-centered spatial audio failed to reach its native haptic channel and preview");
            output.Mix(output.Prepare(&voice, true, 2, 0, new Vector3(90, 5, 200)), samples, 4096, 1);
            Assert(hapticMixCalls == 5 && MathF.Abs(hapticLeftSample - 0.1f) < 0.00001f && hapticRightSample == 0 &&
                hapticFormat == 0, "Ambisonic audio did not use the validated stereo decoder and left haptic channel");
            output.Mix(output.Prepare(&voice, true, 2, 0), samples, 2048, 0);
            Assert(hapticMixCalls == 6 && hapticLeftSample == 0.1f && hapticRightSample == 0.1f && output.UnpositionedBlocks == 1,
                "Unpositioned sounds did not preserve original haptic routing");
            output.Configure(spatialSettings with { ImpactBias = 0.5f }, true, true);
            output.Mix(spatialFrame, samples, 2048, 0);
            Assert(hapticMixCalls == 6 && !output.LastSpatial.Valid, "Spatial settings changed without invalidating an in-flight block and preview");
            var previousOrigin = output.Prepare(&voice, true, 2, 0, new Vector3(110, 5, 200));
            output.Configure(spatialSettings with { ImpactBias = 0.5f, DistanceOrigin = HapticDistanceOrigin.Camera }, true, true);
            output.Mix(previousOrigin, samples, 2048, 0);
            Assert(hapticMixCalls == 6, "Switching distance origin allowed an old in-flight block through");
            var strict = enabled with { Spatial = true };
            output.Configure(strict, true, true);
            output.SetListener(new HapticListenerPose(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp()));
            var cutoff = output.Prepare(&voice, true, 2, 0, new Vector3(25, 0, 0), 55);
            Assert(cutoff.Route == HapticRoute.DistanceCutoff && cutoff.Destination == 0 && output.CutoffBlocks == 1,
                "The 25-yalm cutoff still reached native vibration mixing");
            var unknown = output.Prepare(&voice, true, 2, 0);
            Assert(unknown.Route == HapticRoute.MissingPosition && unknown.Destination == 0,
                "Unknown source position bypassed distance fade through original routing");
            output.SetListener(null);
            Assert(output.Prepare(&voice, true, 2, 0, Vector3.UnitX).Route == HapticRoute.MissingListener,
                "A missing listener bypassed distance fade");
            sends[0].Destination = (nint)replacement;
            Assert(output.Prepare(&voice, true, 2, 0).Route == HapticRoute.Native,
                "An existing native vibration route was mistaken for missing-position plugin output");
            sends[0].Destination = 0x123456;
            output.SetListener(new HapticListenerPose(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp()));
            Assert(output.Prepare(&voice, true, 2, 0, new Vector3(float.NaN, 0, 0)).Route == HapticRoute.MissingPosition,
                "Invalid position coordinates bypassed the cutoff");
            output.Mix(cutoff, samples, 2048, 0); output.Mix(unknown, samples, 2048, 0);
            Assert(hapticMixCalls == 6, "Suppressed haptics still called the native mixer");
            output.Mix(output.Prepare(&voice, true, 2, 0, new Vector3(3, 0, 0), 123), samples, 2048, 0);
            output.Configure(strict, true, true); // The worker publishes its observed impact cue.
            Assert(output.ImpactCue?.SoundId == 123, "A verified audible own impact did not become a guidance cue");
            var cast = output.Prepare(&voice, true, 2, 0, Vector3.Zero, 124);
            Assert(cast.Biased && cast.Spatial.Distance == 0 && cast.Spatial.Pan == 0.25f,
                "Player-origin sound did not gently follow the impact, or its distance was moved");
            output.Mix(cast, samples, 2048, 0);
            Assert(hapticMixCalls == 8 && output.BiasedBlocks == 1 && MathF.Abs(hapticLeftSample - 0.075f) < 0.00001f &&
                hapticRightSample == 0.1f, "Gentle impact bias did not reach the real haptic buffer");
            output.ResetImpactCue();
            output.Mix(cast, samples, 2048, 0);
            Assert(hapticMixCalls == 8 && !output.ImpactCue.HasValue &&
                !output.Prepare(&voice, true, 2, 0, Vector3.Zero, 124).Biased,
                "An actor/focus reset retained impact guidance or an old in-flight block");
            var fullBias = strict with { ImpactBias = 1 };
            output.Configure(fullBias, true, true);
            output.SetListener(new HapticListenerPose(Vector3.Zero, Vector3.UnitX, Stopwatch.GetTimestamp()));
            output.Mix(output.Prepare(&voice, true, 2, 0, new Vector3(3, 0, 0), 125), samples, 2048, 0);
            output.Configure(fullBias, true, true);
            var castFull = output.Prepare(&voice, true, 2, 0, Vector3.Zero, 126);
            output.Mix(castFull, samples, 2048, 0);
            Assert(castFull.Spatial.Pan == 1 && hapticMixCalls == 10 && hapticLeftSample == 0 && hapticRightSample == 0.1f,
                "Configured 100% impact bias did not reach the native output buffer");
            frame = output.Prepare(&voice, true, 2, 0);
            output.Configure(enabled with { Enabled = false }, true, true);
            output.Mix(frame, samples, 2048, 0);
            Assert(hapticMixCalls == 10, "Disable allowed pending haptics to reach the mixer");
        }
        Assert(pcm.SequenceEqual(beforePcm) && beforeVoice.AsSpan().SequenceEqual(new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice))),
            "Added haptics changed audible PCM or the original native sends");
    }

    private static unsafe void CheckSpatialHaptics()
    {
        var now = Stopwatch.GetTimestamp();
        var settings = new HapticSettings(true, Spatial: true);
        var listener = HapticListenerPose.FromCamera(Vector3.Zero, Vector3.Zero, Matrix4x4.Identity, now)!;
        Assert(listener != null && !new HapticSettings().Spatial, "Spatial haptics changed the existing default");
        var automatic = (settings with { NearDistance = 5, FarDistance = 40 }).Normalize();
        Assert(automatic.AutomaticDistance && automatic.NearDistance == 3 && automatic.FarDistance == 25,
            "Existing saved distances did not adopt the new recommendation");
        Assert(automatic.DistanceOrigin == HapticDistanceOrigin.Character &&
            (automatic with { DistanceOrigin = (HapticDistanceOrigin)99 }).Normalize().DistanceOrigin == HapticDistanceOrigin.Character,
            "Character distance is not the default or invalid origins were retained");
        var manual = (settings with { AutomaticDistance = false, NearDistance = 5, FarDistance = 40 }).Normalize();
        Assert(manual.NearDistance == 5 && manual.FarDistance == 40 &&
            (manual with { AutomaticDistance = true }).Normalize().FarDistance == 25,
            "Manual distances or restoring the recommendation failed");
        var malformed = (settings with { AutomaticDistance = false, NearDistance = 200, FarDistance = 1, ImpactBias = float.NaN }).Normalize();
        Assert(malformed.NearDistance == 99 && malformed.FarDistance == 100 && malformed.ImpactBias == 0.25f,
            "Spatial range normalization allowed an invalid denominator");
        Assert((settings with { AutomaticDistance = false, NearDistance = float.NaN, FarDistance = float.NaN }).Normalize().NearDistance == 3,
            "Nonfinite spatial range was retained");
        var right = HapticSpatializer.Evaluate(settings, listener, new Vector3(3, 0, 0), now);
        var left = HapticSpatializer.Evaluate(settings, listener, new Vector3(-3, 0, 0), now);
        Assert(right.Valid && right.Left == 0 && right.Right == 1 && left.Left == 1 && left.Right == 0,
            "Camera left/right mapping is reversed or incomplete");
        var center = HapticSpatializer.Evaluate(settings, listener, Vector3.Zero, now);
        Assert(center.Left == 1 && center.Right == 1 && center.Pan == 0 && center.Distance == 0, "Coincident source/character was not balanced");
        var rear = HapticSpatializer.Evaluate(settings, listener, new Vector3(0, 0, 5), now);
        var front = HapticSpatializer.Evaluate(settings, listener, new Vector3(0, 0, -5), now);
        Assert(rear.Left == rear.Right && front.Left == front.Right, "Front/rear sounds introduced an arbitrary side bias");
        var meleeSource = new Vector3(0.4f, 1.8f, 1.4f);
        var meleeRight = HapticSpatializer.Evaluate(settings, listener, meleeSource, now);
        var originalMeleePan = meleeSource.X / meleeSource.Length();
        Assert(meleeRight.Pan > 0.9f && meleeRight.Pan > originalMeleePan * 5 && meleeRight.Left < 0.1f && meleeRight.Right == 1,
            "Melee haptics still diluted lateral contrast with sound height or amplified the favored grip");
        var floorMelee = HapticSpatializer.Evaluate(settings, listener, new Vector3(0.4f, 0, 1.4f), now);
        Assert(MathF.Abs(meleeRight.Pan - floorMelee.Pan) < 0.00001f,
            "Changing an emitter's height changed its horizontal bearing");
        var meleeLeft = HapticSpatializer.Evaluate(settings, listener, new Vector3(-0.4f, 1.8f, 1.4f), now);
        Assert(MathF.Abs(meleeLeft.Pan + meleeRight.Pan) < 0.00001f && meleeLeft.Left == meleeRight.Right && meleeLeft.Right == meleeRight.Left,
            "Stronger melee separation was not symmetric between grips");
        var farAngle = HapticSpatializer.Evaluate(settings, listener, new Vector3(4, 0, 14), now);
        Assert(meleeRight.Pan > farAngle.Pan && MathF.Abs(farAngle.Pan - 4 / MathF.Sqrt(212)) < 0.00001f,
            "Melee emphasis did not blend back to regular separation at range");
        Assert(HapticSpatializer.Evaluate(settings with { FollowImpact = false }, listener, meleeSource, now) == meleeRight,
            "Disabling impact guidance changed the fixed direct-source separation");
        foreach (var x in new[] { -0.04f, 0f, 0.04f })
        {
            var jitter = HapticSpatializer.Evaluate(settings, listener, new Vector3(x, 1.8f, 0), now);
            Assert(jitter.Pan == 0 && jitter.Left == jitter.Right, "A centered emitter's tiny positional jitter invented a direction");
        }
        var precedingPan = 0f;
        for (var d = 0f; d <= 7; d += 0.01f)
        {
            var continuous = HapticSpatializer.Evaluate(settings, listener, new Vector3(d * 0.3f, 1, d), now);
            Assert(continuous.Valid && continuous.Pan >= 0 && continuous.Pan <= 1 &&
                MathF.Abs(continuous.Pan - precedingPan) < 0.1f,
                "Melee emphasis or its center/range transition produced an abrupt change");
            precedingPan = continuous.Pan;
        }
        var fixedSound = new Vector3(5, 0, -5);
        var yawView = Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitX, Vector3.UnitY);
        var turned = HapticListenerPose.FromCamera(Vector3.Zero, Vector3.Zero, yawView, now)!;
        Assert(HapticSpatializer.Evaluate(settings, listener, fixedSound, now).Pan > 0 &&
            HapticSpatializer.Evaluate(settings, turned, fixedSound, now).Pan < 0,
            "Rotating the camera did not move a fixed world sound between grips");
        var previousGain = 1f;
        for (var distance = 0f; distance <= 60; distance += 0.25f)
        {
            var result = HapticSpatializer.Evaluate(settings, listener, new Vector3(0, 0, -distance), now);
            Assert(result.Valid && result.Left == result.Right && result.Left >= 0 && result.Left <= previousGain,
                "Character distance fade is not bounded and monotonic");
            if (distance <= 3) Assert(result.Left == 1, "Near sound lost full gain");
            if (distance >= 25) Assert(result.Left == 0, "Far sound did not fade out");
            previousGain = result.Left;
        }
        var ranged = HapticSpatializer.Evaluate(automatic, listener, new Vector3(0, 0, -24), now);
        Assert(ranged.Left > 0 && ranged.Left < front.Left &&
            HapticSpatializer.Evaluate(automatic, listener, new Vector3(0, 0, -25), now).Left == 0,
            "The recommended fade removed feedback before its 25-yalm cutoff");
        Assert(HapticSpatializer.Evaluate(manual, listener, new Vector3(0, 0, -30), now).Left > 0,
            "A manual 40-yalm cutoff was overridden by the recommendation");
        var zoomPosition = new Vector3(0, 0, 15);
        var zoomView = Matrix4x4.CreateLookAt(zoomPosition, new Vector3(0, 0, -1), Vector3.UnitY);
        zoomView.M44 = 0; // The game's validated scene camera uses this convention.
        var zoomed = HapticListenerPose.FromCamera(Vector3.Zero, zoomPosition, zoomView, now);
        Assert(zoomed != null && HapticSpatializer.Evaluate(settings, zoomed, new Vector3(0, 0, -5), now) == front,
            "Camera zoom changed the source-to-character distance or grip balance");
        // Offset both camera and character to distinguish source-to-character
        // direction/distance from source-to-camera, including a camera beyond the sound.
        var characterPosition = new Vector3(100, 5, 200);
        var characterSound = characterPosition + new Vector3(3, 0, 0);
        foreach (var cameraOffset in new[] { new Vector3(0, 3, 10), new Vector3(0, 3, 40), new Vector3(20, 8, -20) })
        {
            var cameraPosition = characterPosition + cameraOffset;
            var view = Matrix4x4.CreateLookAt(cameraPosition, cameraPosition - Vector3.UnitZ, Vector3.UnitY);
            var pose = HapticListenerPose.FromCamera(characterPosition, cameraPosition, view, now);
            var result = HapticSpatializer.Evaluate(settings, pose, characterSound, now);
            Assert(pose?.Origin == characterPosition && result.Distance == 3 && result.Pan == 1 && result.Left == 0 && result.Right == 1,
                "Spatial origin followed the camera instead of the character");
        }
        var movedCharacter = HapticListenerPose.FromCamera(new Vector3(0, 0, -15), Vector3.Zero, Matrix4x4.Identity, now);
        Assert(HapticSpatializer.Evaluate(settings, movedCharacter, new Vector3(0, 0, -25), now).Distance == 10 &&
            HapticSpatializer.Evaluate(settings, movedCharacter, new Vector3(0, 0, -25), now).Left > ranged.Left,
            "Moving the character closer did not strengthen feedback with the camera fixed");
        var rotatedResult = HapticSpatializer.Evaluate(settings, turned, fixedSound, now);
        var initialResult = HapticSpatializer.Evaluate(settings, listener, fixedSound, now);
        Assert(rotatedResult.Distance == initialResult.Distance &&
            MathF.Abs(rotatedResult.Left + rotatedResult.Right - initialResult.Left - initialResult.Right) < 0.00001f,
            "Camera facing changed the distance fade while rotating grip balance");
        var cameraSettings = settings with { DistanceOrigin = HapticDistanceOrigin.Camera };
        foreach (var cameraDistance in new[] { 0f, 10f, 20f, 40f })
        {
            var cameraPosition = new Vector3(0, 0, cameraDistance);
            var view = Matrix4x4.CreateLookAt(cameraPosition, cameraPosition - Vector3.UnitZ, Vector3.UnitY);
            var pose = HapticListenerPose.FromCamera(Vector3.Zero, cameraPosition, view, now);
            var range = HapticSpatializer.DistanceRange(cameraSettings, pose);
            Assert(range.Near == cameraDistance + 3 && range.Far == cameraDistance + 25,
                "Camera recommendation did not account for camera-to-character offset");
            var cameraRanged = HapticSpatializer.Evaluate(cameraSettings, pose, new Vector3(0, 0, -24), now);
            Assert(cameraRanged.Distance == cameraDistance + 24 && MathF.Abs(cameraRanged.Left - ranged.Left) < 0.00001f,
                "Camera zoom prematurely attenuated a frontal skill inside the cutoff");
            Assert(HapticSpatializer.Evaluate(cameraSettings, pose, new Vector3(0, 0, -25), now).Left == 0,
                "Recommended camera cutoff did not follow camera zoom");
            // The camera recommendation must keep all nearby effects strong and
            // retain effects within the 25-yalm cutoff around the character.
            for (var angle = 0f; angle < MathF.Tau; angle += MathF.Tau / 24)
            {
                var direction = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
                var melee = HapticSpatializer.Evaluate(cameraSettings, pose, direction * 3, now);
                var atRange = HapticSpatializer.Evaluate(cameraSettings, pose, direction * 24, now);
                Assert(MathF.Abs(Math.Max(melee.Left, melee.Right) - 1) < 0.00001f && Math.Max(atRange.Left, atRange.Right) > 0,
                    "Camera recommendation lost the character's melee/ranged combat envelope");
                var charResult = HapticSpatializer.Evaluate(settings, pose, direction * 24, now);
                Assert(MathF.Abs(atRange.Pan - charResult.Pan) < 0.00001f,
                    "Selecting camera distance changed the character-centered direction origin");
            }
        }
        var cameraManual = (manual with { DistanceOrigin = HapticDistanceOrigin.Camera }).Normalize();
        var fixedManualRange = HapticSpatializer.DistanceRange(cameraManual, zoomed);
        Assert(fixedManualRange.Near == 5 && fixedManualRange.Far == 40,
            "Camera compensation overwrote absolute manual limits");
        Assert(HapticSpatializer.Evaluate(cameraSettings, zoomed, new Vector3(0, 0, -20), now).Left >
            HapticSpatializer.Evaluate(cameraManual, zoomed, new Vector3(0, 0, -20), now).Left,
            "Manual camera fade and zoom-aware recommendation were not independent");
        Assert(HapticListenerPose.FromCamera(Vector3.Zero, Vector3.One, Matrix4x4.Identity, now) == null &&
            HapticListenerPose.FromCamera(Vector3.Zero, Vector3.Zero, default, now) == null &&
            HapticListenerPose.FromCamera(new Vector3(float.NaN, 0, 0), Vector3.Zero, Matrix4x4.Identity, now) == null,
            "A mismatched/invalid camera matrix was accepted");
        Assert(!HapticSpatializer.Evaluate(settings, listener, Vector3.Zero, now + Stopwatch.Frequency).Valid &&
            !HapticSpatializer.Evaluate(settings, listener, new Vector3(float.NaN, 0, 0), now).Valid &&
            !HapticSpatializer.Evaluate(settings, null, Vector3.Zero, now).Valid, "Invalid or stale spatial input was accepted");
        var neutral = HapticSpatializer.Evaluate(settings with { DistanceFalloff = false }, listener, new Vector3(0, 0, -100), now);
        Assert(neutral.Left == 1 && neutral.Right == 1, "Disabling fade did not retain full gain for a centered distant source");
        foreach (var channels in new[] { 8, 16 })
        {
            var pcm = new float[256 * channels]; var matrix = new float[2 * channels];
            matrix[0] = matrix[channels + 1] = 1;
            for (var i = 0; i < 256; i++) pcm[i * channels] = pcm[i * channels + 1] = MathF.Sin(i * 0.1f) * 0.2f;
            var original = (float[])pcm.Clone(); var output = new float[2048];
            Assert(HapticSpatializer.Render(pcm, matrix, output, right), "Supported native matrix failed spatial rendering");
            for (var i = 0; i < 256; i++)
            {
                Assert(output[i * 8] == 0 && MathF.Abs(output[i * 8 + 1] - pcm[i * channels]) < 0.00001f,
                    "Spatial rendering lost waveform timing or channel balance");
                Assert(output.AsSpan(i * 8 + 2, 6).IndexOfAnyExcept(0f) < 0, "Spatial haptics leaked into other native channels");
            }
            Assert(pcm.SequenceEqual(original), "Spatial haptics modified audible source PCM");
            pcm[0] = float.PositiveInfinity;
            Assert(!HapticSpatializer.Render(pcm, matrix, output, right) && output.All(x => x == 0), "Invalid spatial PCM reached native processing");
        }
        var pool = stackalloc GameAudioLayout.SoundData[GameAudioLayout.PlayerSoundOwnership.Capacity];
        new Span<GameAudioLayout.SoundData>(pool, GameAudioLayout.PlayerSoundOwnership.Capacity).Clear();
        var ownership = new GameAudioLayout.PlayerSoundOwnership(pool);
        ownership.SetPlayer(0x123, 42, 0x456);
        pool[0].IsActive = 1; pool[0].SoundId = -20; pool[0].IsPositional = 1; pool[0].Position = new Vector3(12, 34, 56);
        ownership.Acquired(pool, ownership.Player); ownership.LinkPlayback(-20, 77);
        Assert(ownership.TryGetPosition(77, out var position) && position == new Vector3(12, 34, 56),
            "Positional sound lost logical-to-playback ownership mapping");
        pool[0].IsPositional = 0;
        Assert(!ownership.TryGetPosition(77, out _), "A nonpositional sound invented spatial coordinates");
        pool[0].IsPositional = 1; ownership.Acquired(pool, null);
        Assert(!ownership.TryGetPosition(77, out _), "A reused foreign slot leaked its spatial coordinates");
    }

    private static void CheckImpactBias()
    {
        var now = Stopwatch.GetTimestamp();
        var settings = new HapticSettings(true, Spatial: true);
        var listener = new HapticListenerPose(Vector3.Zero, Vector3.UnitX, now);
        var source = new Vector3(0, 1, 0);
        var original = HapticSpatializer.Evaluate(settings, listener, source, now);
        Assert(settings.ImpactBias == 0.25f &&
            (settings with { ImpactBias = 0 }).Normalize().ImpactBias == 0.25f &&
            (settings with { ImpactBias = 2 }).Normalize().ImpactBias == 1 &&
            (settings with { ImpactBias = float.NaN }).Normalize().ImpactBias == 0.25f,
            "Impact bias default/minimum/maximum normalization changed");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<HapticSettings>(
            "{\"Enabled\":true,\"Spatial\":true,\"Directionality\":0}")!.Normalize();
        var legacyRight = HapticSpatializer.Evaluate(legacy, listener, new Vector3(3, 0, 0), now);
        Assert(legacy.ImpactBias == 0.25f && legacyRight.Pan == 1 && legacyRight.Left == 0 && legacyRight.Right == 1,
            "Legacy separation settings affected the fixed full-separation behavior or lost default impact bias");
        foreach (var amount in new[] { 0.25f, 0.5f, 0.75f, 1f })
        {
            var result = HapticSpatializer.BiasTowardImpact(settings with { ImpactBias = amount }, listener,
                source, original, new Vector3(3, 0, 0));
            Assert(result.Pan == amount && result.Left == 1 - amount && result.Right == 1 && result.Distance == original.Distance,
                "Adjusting impact bias did not preserve direction endpoints, source distance and maximum gain");
        }
        var biased = HapticSpatializer.BiasTowardImpact(settings, listener, source, original, new Vector3(3, 0, 0));
        Assert(biased.Pan == 0.25f && biased.Left == 0.75f && biased.Right == 1 && biased.Distance == original.Distance,
            "Gentle impact following moved the cast source or did not apply its 25% bias");
        var left = HapticSpatializer.BiasTowardImpact(settings, listener, source, original, new Vector3(-3, 0, 0));
        Assert(left.Pan == -biased.Pan && left.Left == biased.Right && left.Right == biased.Left,
            "Player-origin impact bias was not symmetric");
        Assert(HapticSpatializer.BiasTowardImpact(settings with { FollowImpact = false }, listener, source, original, Vector3.UnitX) == original &&
            HapticSpatializer.BiasTowardImpact(settings, listener, source, original, new Vector3(25, 0, 0)) == original,
            "Disabled guidance or an inaudible far impact still biased a cast");
        var directSource = Vector3.UnitX;
        var direct = HapticSpatializer.Evaluate(settings, listener, directSource, now);
        Assert(HapticSpatializer.BiasTowardImpact(settings, listener, directSource, direct, new Vector3(-3, 0, 0)) == direct,
            "A real off-center source was steered away from its own position");
        var tracker = new HapticImpactTracker(); var epoch = new object();
        tracker.Observe(epoch, 1, new Vector3(3, 0, 0), 0.05f, now);
        tracker.Observe(epoch, 2, new Vector3(-3, 0, 0), 0.2f, now);
        tracker.Observe(epoch, 3, new Vector3(0, 0, 3), 0.1f, now);
        Assert(tracker.TryTake(epoch, now, out var cue) && cue.SoundId == 2,
            "Impact guidance did not select the strongest audible cue in the worker interval");
        Assert(!tracker.TryTake(epoch, now, out _), "An impact cue was published twice without fresh audio");
        tracker.Observe(epoch, 4, Vector3.UnitX, 0.01f, now);
        Assert(tracker.TryTake(epoch, now, out cue) && cue.SoundId == 4, "A previous loud cue masked fresh quieter impacts");
        tracker.Observe(epoch, 5, Vector3.UnitX, 0.2f, now);
        Assert(!tracker.TryTake(new object(), now, out _), "Old actor/focus cue crossed an epoch boundary");
        tracker.Observe(epoch, 6, Vector3.UnitX, 0.2f, now);
        Assert(!tracker.TryTake(epoch, now + Stopwatch.Frequency / 5, out _), "Stale impact guidance remained active");
        tracker.Observe(epoch, 7, Vector3.UnitX, float.NaN, now);
        tracker.Observe(epoch, 8, new Vector3(float.NaN, 0, 0), 0.5f, now);
        tracker.Observe(epoch, 9, Vector3.UnitX, 0.001f, now);
        Assert(!tracker.TryTake(epoch, now, out _), "Invalid or inaudibly quiet audio created an impact cue");
        tracker.Observe(epoch, 10, Vector3.UnitX, 0.2f, now); tracker.TryTake(epoch, now, out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        { tracker.Observe(epoch, 10, Vector3.UnitX, 0.2f, now); tracker.TryTake(epoch, now, out _); }
        Assert(GC.GetAllocatedBytesForCurrentThread() == allocated, "Impact audio observations allocated managed memory");
    }

    private static void CheckNativeStereoMatrices()
    {
        // Captured from the installed game's real native 8 -> 2 builder. Each
        // input has an eight-lane padded output row, including intentional crossfeed.
        var native = new float[]
        {
            .89f,.11f,0,0,0,0,0,0,
            .11f,.89f,0,0,0,0,0,0,
            .58f,.58f,0,0,0,0,0,0,
            .385f,.385f,0,0,0,0,0,0,
            .92f,0,0,0,0,0,0,0,
            0,.92f,0,0,0,0,0,0,
            0,0,0,0,0,0,0,0,
            0,0,0,0,0,0,0,0,
        };
        var expected = new float[] { .89f,.11f,.58f,.385f,.92f,0,0,0, .11f,.89f,.58f,.385f,0,.92f,0,0 };
        var decoded = HapticStereoMatrix.FromNative(native, 8);
        Assert(decoded.SequenceEqual(expected), "Padded native input rows or intentional crossfeed were decoded incorrectly");
        var extended = new float[256];
        extended[0] = .5f; extended[1] = .5f; extended[16] = .5f; extended[17] = -.5f;
        var extendedDecoded = HapticStereoMatrix.FromNative(extended, 16);
        Assert(extendedDecoded[0] == .5f && extendedDecoded[1] == .5f &&
            extendedDecoded[16] == .5f && extendedDecoded[17] == -.5f &&
            extendedDecoded.Where((_, i) => i is not (0 or 1 or 16 or 17)).All(x => x == 0),
            "Native 16-channel row stride or signed coefficients were lost");
        foreach (var channels in new[] { 8, 16 })
        {
            var matrix = channels == 8 ? decoded : extendedDecoded;
            var samples = new float[256 * channels]; var output = new float[2048];
            for (var input = 0; input < channels; input++)
            {
                Array.Clear(samples); samples[input] = 1;
                Assert(HapticSpatializer.Render(samples, matrix, output, new HapticSpatialFrame(true, 0, 0, 1, 1)),
                    "Native coefficient fixture failed spatial rendering");
                var mono = (matrix[input] + matrix[channels + input]) * .5f;
                Assert(output[0] == mono && output[1] == mono,
                    "An input channel's native stereo contribution was dropped or transposed");
            }
        }
        var rejected = false;
        try { HapticStereoMatrix.FromNative(native.AsSpan(0, 16), 8); }
        catch (NotSupportedException) { rejected = true; }
        Assert(rejected, "A compact matrix was accepted as a padded native matrix");
        native[0] = float.NaN; rejected = false;
        try { HapticStereoMatrix.FromNative(native, 8); }
        catch (NotSupportedException) { rejected = true; }
        Assert(rejected, "Nonfinite native coefficients were accepted");
        native[0] = .11f; native[1] = .89f; rejected = false;
        try { HapticStereoMatrix.FromNative(native, 8); }
        catch (NotSupportedException) { rejected = true; }
        Assert(rejected, "Reversed native handedness was accepted");
        // Replay the actual v0.9.5 trace's distinct cast and enemy-impact positions.
        var now = Stopwatch.GetTimestamp();
        var character = new Vector3(43.253796f, 504.92773f, 217.43387f);
        var enemy = new Vector3(62.876038f, 504.92706f, 199.64899f);
        var listener = new HapticListenerPose(character, new Vector3(.56606084f, 0, .8243635f), now);
        var settings = new HapticSettings(true, Spatial: true);
        var impact = HapticSpatializer.Evaluate(settings, listener, enemy, now);
        var cast = HapticSpatializer.Evaluate(settings, listener, character, now);
        Assert(MathF.Abs(impact.Distance - 26.4827f) < .001f && impact.Left == 0 && impact.Right == 0,
            "The recorded enemy-impact position did not obey the 25-yalm cutoff");
        Assert(cast.Distance == 0 && cast.Left == 1 && cast.Right == 1,
            "The enemy cutoff incorrectly removed a separate cast sound located at the character");
    }

    private static async Task CheckSfxSession()
    {
        foreach (var mode in new[] { LightingEffect.SfxLoudness, LightingEffect.SfxColorBlend })
            await CheckSfxSession(mode);
    }

    private static async Task CheckSfxSession(LightingEffect mode)
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground);
        devices.OnOpen = () => devices.Opened.Last().AllowBackground = true;
        using var session = new LightingSession(devices, () => foreground, _ => throw new Exception("Unexpected SFX HID failure"));
        var settings = LightingSettings.Default with { Effect = mode, PlayerLights = 10,
            Red = 255, Green = 0, Blue = 0, SecondaryRed = 0, SecondaryGreen = 0, SecondaryBlue = 255 };
        await session.TickAsync(settings, 0, default, 0);
        var quiet = devices.Opened.Last().Reports.Last();
        var baseline = mode == LightingEffect.SfxColorBlend ? 255 : 0;
        Assert(quiet[45] == baseline && quiet[46] == 0 && quiet[47] == 0, "SFX mode has an incorrect quiet-state hardware color");
        await session.TickAsync(settings, 0.033, default, 0.8f);
        var preview = ControllerPreviewGeometry.Frame(settings, true, 0.033, 0.8f);
        var report = devices.Opened.Last().Reports.Last();
        Assert(report[45] == preview.Red && report[46] == preview.Green && report[47] == preview.Blue && report[44] == preview.PlayerLights,
            "The actual SFX output report diverged from the preview");
        foreground = false;
        await session.TickAsync(settings, 0.066, default, 1);
        await session.TickAsync(settings, 0.1, default, 1);
        Assert(session.ReleaseCount == 1 && session.WriteCount == 2 && devices.Opened.All(d => d.Disposed) &&
            devices.Opened.Last().Reports.Last().Skip(44).Take(4).All(b => b == 0), "SFX did not turn off once and release on focus loss");
        foreground = true;
        await session.TickAsync(settings, 1, default, 0);
        var resumed = devices.Opened.Last().Reports.Last();
        Assert(resumed[45] == baseline && resumed[46] == 0 && resumed[47] == 0, "SFX did not return to its own quiet state after focus return");
        devices.Paths = []; await session.TickAsync(settings, 2, default, 0);
        devices.Paths = ["controller"]; await session.TickAsync(settings, 3, default, 0.5f);
        var reconnect = devices.Opened.Last().Reports.Last();
        Assert(reconnect[45] == 128 && reconnect[46] == 0 && reconnect[47] == (mode == LightingEffect.SfxColorBlend ? 128 : 0) && reconnect[44] == 10,
            "The selected SFX mode did not recover after replugging");
        var switched = settings with { Effect = mode == LightingEffect.SfxColorBlend ? LightingEffect.SfxLoudness : LightingEffect.SfxColorBlend };
        await session.TickAsync(switched, 3.033, default, 0.5f);
        Assert(devices.Opened.Last().Reports.Last()[47] == (switched.Effect == LightingEffect.SfxColorBlend ? 128 : 0),
            "Switching between the separate SFX options did not change controller output");
        await session.TickAsync(settings with { Enabled = false }, 3.1, default, 1);
        Assert(session.ReleaseCount == 2 && session.DeviceCount == 0, "Disabling SFX did not perform lights-off release");
        foreach (var device in devices.Opened)
        foreach (var sent in device.Reports)
            Assert(sent[1] == 0 && sent.Skip(3).Take(36).All(b => b == 0), "SFX session wrote native haptic or audio controls");
    }

    private static async Task CheckOwnership()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground);
        devices.OnOpen = () => devices.Opened.Last().AllowBackground = true;
        using var session = new LightingSession(devices, () => foreground, _ => throw new Exception("Unexpected output failure"));
        await session.TickAsync(LightingSettings.Default, 0, default);
        Assert(session.DeviceCount == 1 && session.WriteCount == 1, "Foreground did not acquire lighting");
        await session.TickAsync(LightingSettings.Default, 0.1, default);
        Assert(session.WriteCount == 1, "Unchanged solid color was repeatedly written");
        await session.TickAsync(LightingSettings.Default with { Blue = 50 }, 0.2, default);
        Assert(session.WriteCount == 2, "Settings were not applied");
        foreground = false;
        var scans = devices.ScanCount;
        for (var i = 0; i < 100; i++) await session.TickAsync(LightingSettings.Default, i + 1, default);
        Assert(session.DeviceCount == 0 && devices.Opened.All(d => d.Disposed), "Background retained a device handle");
        Assert(session.WriteCount == 2 && devices.ScanCount == scans, "Background wrote or discovered devices");
        foreground = true;
        await session.TickAsync(LightingSettings.Default, 102, default);
        Assert(session.DeviceCount == 1 && session.WriteCount == 3, "Focus return did not reapply lighting");
        await session.TickAsync(LightingSettings.Default with { Enabled = false }, 103, default);
        Assert(session.DeviceCount == 0 && session.WriteCount == 3, "Disabling did not stop output");
    }

    private static void CheckPlayerAnimations()
    {
        byte[] legacy = [0, 4, 10, 21, 27, 31];
        for (var count = 0; count <= 5; count++)
        {
            var mask = PlayerIndicatorEffects.MaskForCount(count);
            Assert(mask == legacy[count] && PlayerIndicatorEffects.Count(mask) == count, "Legacy player layout changed");
            foreach (var animation in Enum.GetValues<PlayerIndicatorAnimation>())
            {
                var settings = LightingSettings.Default with { PlayerLights = mask, PlayerAnimation = animation, PlayerCycleSeconds = 2 };
                var seen = new HashSet<byte>();
                for (var i = 0; i < 160; i++)
                {
                    var time = i / 80.0;
                    var frame = LightingEffects.Render(settings, time);
                    var lit = PlayerIndicatorEffects.Count(frame.PlayerLights);
                    Assert(frame.PlayerLights <= 31 && lit <= count, "Animation exceeded the configured count or physical strip");
                    if (animation is PlayerIndicatorAnimation.Steady or PlayerIndicatorAnimation.Chase or PlayerIndicatorAnimation.Blink or PlayerIndicatorAnimation.Loading)
                        Assert((frame.PlayerLights & ~mask) == 0, "Selected-position animation left its selected pool");
                    Assert(frame.Red == settings.Red && frame.Green == settings.Green && frame.Blue == settings.Blue, "Player animation altered the RGB effect");
                    Assert(ControllerPreviewGeometry.Frame(settings, true, time) == frame, "Player preview differs from the hardware frame");
                    Assert(LightingEffects.Render(settings, time + 2).PlayerLights == frame.PlayerLights, $"Player animation does not repeat: {animation}, count {count}, time {time}");
                    Assert(frame.PlayerLightsImmediate == (animation != PlayerIndicatorAnimation.Steady), "Animated update/fade mode incorrect");
                    if (animation == PlayerIndicatorAnimation.Steady) Assert(lit == count, "Steady layout changed the selected count");
                    if (animation == PlayerIndicatorAnimation.Chase)
                        Assert(lit == Math.Min(count, 1), "Traveling effect did not move a single light within the pool");
                    if (animation == PlayerIndicatorAnimation.Bounce) Assert(lit == count, "Bounce did not move the configured number together");
                    if (animation is PlayerIndicatorAnimation.Heartbeat or PlayerIndicatorAnimation.Sparkle)
                        Assert(lit == 0 || lit == count, "Pulse/sparkle did not light the configured number at once");
                    if (count == 0) Assert(frame.PlayerLights == 0, "Off count produced a lit indicator");
                    seen.Add(frame.PlayerLights);
                }
                if (count > 0 && animation != PlayerIndicatorAnimation.Steady &&
                    !(count == 1 && animation == PlayerIndicatorAnimation.Chase) &&
                    !(count == 5 && animation == PlayerIndicatorAnimation.Bounce))
                    Assert(seen.Count > 1, "Animation never changed");
            }
        }

        byte Render(int count, PlayerIndicatorAnimation animation, double time) => PlayerIndicatorEffects.Render(
            PlayerIndicatorEffects.MaskForCount(count), animation, 2, time);
        Assert(Render(2, PlayerIndicatorAnimation.Chase, 0) == 2 && Render(2, PlayerIndicatorAnimation.Chase, 1.7) == 8, "Chase left the selected inner pair");
        byte[] fourChase = [1, 2, 8, 16];
        for (var i = 0; i < fourChase.Length; i++) Assert(Render(4, PlayerIndicatorAnimation.Chase, i * 0.5) == fourChase[i], "Four-light chase used the excluded center LED");
        byte[] bounce = [1, 2, 4, 8, 16, 8, 4, 2];
        for (var i = 0; i < bounce.Length; i++) Assert(Render(1, PlayerIndicatorAnimation.Bounce, i * 0.25) == bounce[i], "Bounce no longer travels across the full strip");
        Assert(Render(4, PlayerIndicatorAnimation.Bounce, 0) == 15 && Render(4, PlayerIndicatorAnimation.Bounce, 1) == 30, "Four-light bounce did not move four lights together");
        Assert(Render(5, PlayerIndicatorAnimation.Bounce, 0) == 31 && Render(5, PlayerIndicatorAnimation.Bounce, 1) == 31, "Full-strip bounce should stay fully lit");
        var sparklePositions = new HashSet<byte>();
        for (var i = 0; i < 8; i++) sparklePositions.Add(Render(1, PlayerIndicatorAnimation.Sparkle, i * 0.25));
        Assert(sparklePositions.Count > 1 && sparklePositions.Any(mask => mask != 4), "Single-light sparkle was incorrectly restricted to the center");
        Assert(Render(3, PlayerIndicatorAnimation.Blink, 0.2) == 21 && Render(3, PlayerIndicatorAnimation.Blink, 1.2) == 0, "Blink pattern incorrect");
        Assert(Render(2, PlayerIndicatorAnimation.Heartbeat, 0.1) == 10 && Render(2, PlayerIndicatorAnimation.Heartbeat, 0.54) == 10 &&
            Render(2, PlayerIndicatorAnimation.Heartbeat, 0.3) == 0 && Render(2, PlayerIndicatorAnimation.Heartbeat, 1.2) == 0, "Heartbeat is not two distinct pulses");
        byte[] loading = [0, 1, 5, 21];
        for (var i = 0; i < loading.Length; i++) Assert(Render(3, PlayerIndicatorAnimation.Loading, i * 0.5) == loading[i], "Loading does not fill up to the selected count");
        var rainbow = LightingSettings.Default with { Effect = LightingEffect.Rainbow, PlayerLights = 10, PlayerAnimation = PlayerIndicatorAnimation.Chase };
        Assert(LightingEffects.Render(rainbow, 0.3).PlayerLights == LightingEffects.Render(rainbow with { CycleSeconds = 60 }, 0.3).PlayerLights, "RGB speed changes the indicator speed");
        var normalized = (rainbow with { PlayerAnimation = (PlayerIndicatorAnimation)99, PlayerCycleSeconds = float.NaN }).Normalize();
        Assert(normalized.PlayerAnimation == PlayerIndicatorAnimation.Steady && normalized.PlayerCycleSeconds == 2, "Invalid indicator settings not normalized");

        foreach (var length in new[] { 48, 63, 64 })
        foreach (var mask in new byte[] { 0, 1, 10, 21, 31 })
        {
            var original = new byte[length];
            new Random(56).NextBytes(original); original[0] = 2;
            var steady = (byte[])original.Clone();
            var animated = (byte[])original.Clone();
            DualSenseReport.Apply(steady, true, true, new LightingFrame(11, 22, 33, mask));
            DualSenseReport.Apply(animated, true, true, new LightingFrame(11, 22, 33, mask, true));
            Assert(animated[44] == (byte)(mask | 0x20), "Animated/off frame is missing the no-fade flag");
            for (var i = 0; i < length; i++) if (i != 44)
                Assert(animated[i] == steady[i], "Indicator animation changed another native field");
        }
    }

    private static async Task CheckAnimatedIndicatorSession()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground);
        devices.OnOpen = () => devices.Opened.Last().AllowBackground = true;
        using var session = new LightingSession(devices, () => foreground, _ => throw new Exception("Unexpected animation output failure"));
        var settings = LightingSettings.Default with { PlayerLights = 27, PlayerAnimation = PlayerIndicatorAnimation.Chase, PlayerCycleSeconds = 2 };
        await session.TickAsync(settings, 0, default);
        await session.TickAsync(settings, 0.51, default);
        await session.TickAsync(settings, 1.01, default);
        Assert(session.WriteCount == 3 && devices.Opened.Single().Reports.Select(r => r[44]).SequenceEqual(new byte[] { 33, 34, 40 }), "Indicator animation used the excluded center or failed with solid RGB");
        foreground = false;
        await session.TickAsync(settings, 1.1, default);
        Assert(session.ReleaseCount == 1 && devices.Opened.Single().Reports.Last().Skip(44).Take(4).All(b => b == 0) && devices.Opened.Single().Disposed, "Animated lighting did not turn off and release control");
        await session.TickAsync(settings, 1.5, default);
        Assert(session.ReleaseCount == 1 && session.WriteCount == 3, "Player animation kept writing in the background");
        foreground = true;
        await session.TickAsync(settings, 2, default);
        Assert(session.WriteCount == 4 && devices.Opened.Last().Reports.Last()[44] == 33, "Player animation did not resume on focus return");
        devices.Paths = [];
        await session.TickAsync(settings, 3, default);
        devices.Paths = ["controller"];
        await session.TickAsync(settings, 4, default);
        Assert(session.WriteCount == 5 && devices.Opened.Last().Reports.Last()[44] == 33, "Saved player animation did not return after replugging");
    }

    private static void CheckControllerPreview()
    {
        using var svg = typeof(Program).Assembly.GetManifestResourceStream(ControllerPreviewGeometry.SvgResource)
            ?? throw new Exception("Missing embedded SVG artwork");
        var geometry = ControllerPreviewGeometry.Load(svg);
        Assert(geometry.Size.X / geometry.Size.Y is > 2 and < 2.2f, "Touchpad crop aspect ratio changed");
        Assert(geometry.Indicators.Length == 5, "Not all five player LEDs are mapped");
        var centers = geometry.Indicators.Select(i => i.Position.X + i.Size.X / 2).ToArray();
        Assert(Math.Abs(centers[2] - geometry.Size.X / 2) < 0.01f, "Player 1 LED is not centered");
        Assert(Math.Abs(centers[0] + centers[4] - geometry.Size.X) < 0.01f &&
            Math.Abs(centers[1] + centers[3] - geometry.Size.X) < 0.01f, "Player LED positions are asymmetric");
        Assert(centers[4] - centers[0] > geometry.Size.X / 3, "Player LEDs collapsed into an inaccurate central cluster");
        foreach (var indicator in geometry.Indicators)
        {
            Assert(indicator.Position.X >= 0 && indicator.Position.Y >= 0 && indicator.Position.X + indicator.Size.X <= geometry.Size.X &&
                indicator.Position.Y + indicator.Size.Y <= geometry.Size.Y, "Player LED lies outside the drawing");
        }
        foreach (var mask in new byte[] { 0, 4, 10, 21, 27, 31 })
        {
            var frame = new LightingFrame(0, 0, 0, mask);
            Assert(geometry.Indicators.Count(i => ControllerPreviewGeometry.IsPlayerLit(frame, i.Bit)) == System.Numerics.BitOperations.PopCount(mask), "Preview player count differs from the hardware mask");
        }
        var one = new LightingFrame(0, 0, 0, 4);
        Assert(ControllerPreviewGeometry.IsPlayerLit(one, 2) && !ControllerPreviewGeometry.IsPlayerLit(one, 0), "Player 1 lights the wrong position");
        foreach (var effect in Enum.GetValues<LightingEffect>())
        for (var i = 0; i < 90; i++)
        {
            var settings = LightingSettings.Default with { Effect = effect, Brightness = 0.35f, PlayerLights = 21 };
            var time = i * 0.137;
            Assert(ControllerPreviewGeometry.Frame(settings, true, time) == LightingEffects.Render(settings, time), "Preview colors or animation phase diverged from hardware rendering");
        }
        Assert(ControllerPreviewGeometry.Frame(LightingSettings.Default, false, 1) == default, "Custom preview animates in the background");
        Assert(ControllerPreviewGeometry.Frame(LightingSettings.Default with { Enabled = false }, true, 1) == default, "Disabled lighting is shown as active");
        Assert(ControllerPreviewGeometry.Frame(LightingSettings.Default with { Brightness = 0, PlayerLights = 4 }, true, 1).PlayerLights == 4, "RGB brightness incorrectly dims the white player LEDs");
        foreach (var resource in new[] { ControllerPreviewGeometry.PngResource, ControllerPreviewGeometry.LightbarResource })
        {
            using var texture = typeof(Program).Assembly.GetManifestResourceStream(resource)
                ?? throw new Exception("Missing embedded preview texture: " + resource);
            var header = new byte[24];
            texture.ReadExactly(header);
            Assert(header.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Preview resource is not a PNG");
            var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16, 4));
            var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20, 4));
            Assert(width == (uint)geometry.Size.X && height == (uint)geometry.Size.Y, "Preview body, lightbar mask and SVG coordinates have different sizes");
        }
    }

    private static async Task CheckGlobalRelease()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground);
        devices.OnOpen = () => devices.Opened.Last().AllowBackground = true;
        using var session = new LightingSession(devices, () => foreground,
            _ => throw new Exception("Unexpected global release error"));
        var settings = LightingSettings.Default with { Effect = LightingEffect.Rainbow, PlayerLights = 31 };
        await session.TickAsync(settings, 0, default);
        foreground = false;
        await session.TickAsync(settings, 0.1, default);
        var device = devices.Opened.Single();
        var release = device.Reports.Last();
        Assert(session.ReleaseCount == 1 && device.Disposed && session.DeviceCount == 0,
            "Global release did not close the controller after focus loss");
        Assert(release[44] == 0 && release.Skip(45).Take(3).All(b => b == 0),
            "Global release left a custom RGB color or player indicator on");
        Assert(release[1] == 0 && release[2] == 0x14 && release.Skip(3).Take(36).All(b => b == 0),
            "Lights-off update enabled native haptic/audio/trigger changes");
        for (var i = 1; i <= 50; i++) await session.TickAsync(settings, i, default);
        devices.Paths = [];
        await session.TickAsync(settings, 51, default);
        devices.Paths = ["controller"];
        await session.TickAsync(settings, 52, default);
        Assert(device.Reports.Count == 2 && devices.Opened.Count == 1 && session.ReleaseCount == 1,
            "Background updates or reconnect interfered with another lighting app");
        foreground = true;
        await session.TickAsync(settings, 56, default);
        Assert(session.WriteCount == 2 && devices.Opened.Last().Reports.Last()[45] == 255,
            "Returning to FFXIV did not resume the saved custom effect");
        await session.TickAsync(settings with { Enabled = false }, 57, default);
        Assert(session.ReleaseCount == 2 && devices.Opened.Last().Disposed,
            "Disabling lighting did not perform the same automatic release");
        await session.TickAsync(settings, 64, default);
        await session.ReleaseAsync(true, default);
        await session.ReleaseAsync(true, default);
        Assert(session.ReleaseCount == 3 && devices.Opened.Last().Disposed,
            "Unloading failed to release lighting once or repeated the release");
    }

    private static async Task CheckReleaseRegainFocus()
    {
        var foreground = true;
        var regainFocus = false;
        bool IsForeground()
        {
            if (!regainFocus) return foreground;
            regainFocus = false;
            foreground = true;
            return false; // Focus returns between the worker check and the release check.
        }
        var devices = new FakeDevices(() => foreground);
        using var session = new LightingSession(devices, IsForeground, _ => throw new Exception("Unexpected focus release error"));
        await session.TickAsync(LightingSettings.Default, 0, default);
        foreground = false;
        regainFocus = true;
        await session.TickAsync(LightingSettings.Default, 0.1, default);
        Assert(session.ReleaseCount == 0 && devices.Opened.Single().Reports.Count == 1 && devices.Opened.Single().Disposed,
            "Release cleared lighting after FFXIV regained focus or kept a handle open");
        await session.TickAsync(LightingSettings.Default, 0.2, default);
        Assert(session.WriteCount == 2, "Foreground did not recover after a skipped release");
    }

    private static async Task CheckReleaseFailure()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground);
        devices.OnOpen = () => devices.Opened.Last().AllowBackground = true;
        var errors = 0;
        using var session = new LightingSession(devices, () => foreground, _ => errors++);
        await session.TickAsync(LightingSettings.Default, 0, default);
        devices.Opened.Single().Fail = true;
        foreground = false;
        for (var i = 1; i <= 20; i++) await session.TickAsync(LightingSettings.Default, i, default);
        Assert(errors == 1 && session.ReleaseCount == 0 && devices.Opened.Single().Disposed,
            "Failed lights-off write kept a handle or retried over another app");
    }

    private static async Task CheckFocusRace()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground) { OnOpen = () => foreground = false };
        using var session = new LightingSession(devices, () => foreground, _ => { });
        await session.TickAsync(LightingSettings.Default, 0, default);
        Assert(session.WriteCount == 0 && session.DeviceCount == 0 && devices.Opened.All(d => d.Disposed), "Focus lost during discovery still sent lighting");
    }

    private static async Task CheckSolidRefresh()
    {
        var devices = new FakeDevices(() => true);
        using var session = new LightingSession(devices, () => true, _ => { });
        await session.TickAsync(LightingSettings.Default, 0, default);
        await session.TickAsync(LightingSettings.Default, 0.5, default);
        Assert(session.WriteCount == 1, "Solid refresh too frequent");
        await session.TickAsync(LightingSettings.Default, 1.1, default);
        Assert(session.WriteCount == 2, "Solid lighting did not refresh after an external reconnect write");
    }

    private static async Task CheckMultipleControllers()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground) { Paths = ["first", "second"] };
        devices.OnOpen = () =>
        {
            devices.Opened.Last().AllowBackground = true;
            devices.Opened.Last().OnWrite = () => foreground = false;
        };
        using var session = new LightingSession(devices, () => foreground, _ => { });
        await session.TickAsync(LightingSettings.Default, 0, default);
        Assert(session.WriteCount == 1 && session.ReleaseCount == 1 && session.DeviceCount == 0 && devices.Opened.All(d => d.Disposed), "Focus lost between controllers still sent another custom write");
        Assert(devices.Opened[0].Reports.Last().Skip(44).Take(4).All(b => b == 0) && devices.Opened[1].Reports.Count == 0,
            "Release did not clear the changed controller or wrote to an untouched controller");
    }

    private static async Task CheckReconnectAndFailure()
    {
        var devices = new FakeDevices(() => true);
        var errors = 0;
        using var session = new LightingSession(devices, () => true, _ => errors++);
        await session.TickAsync(LightingSettings.Default, 0, default);
        devices.Paths = [];
        await session.TickAsync(LightingSettings.Default, 1, default);
        Assert(session.DeviceCount == 0 && devices.Opened[0].Disposed, "Unplug not detected");
        devices.Paths = ["controller"];
        await session.TickAsync(LightingSettings.Default, 2, default);
        Assert(session.DeviceCount == 1 && session.WriteCount == 2, "Replug did not reapply saved lighting");
        devices.Opened.Last().Fail = true;
        await session.TickAsync(LightingSettings.Default with { Red = 1 }, 2.1, default);
        Assert(session.DeviceCount == 0 && errors == 1, "Failed output not closed");
        await session.TickAsync(LightingSettings.Default, 3, default);
        Assert(session.DeviceCount == 1 && session.WriteCount == 3, "Failed output did not recover");
    }

    private static async Task CheckCancellation()
    {
        var devices = new FakeDevices(() => true);
        using var session = new LightingSession(devices, () => true, _ => { });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await session.TickAsync(LightingSettings.Default, 0, cancelled.Token); Assert(false, "Cancelled worker continued"); }
        catch (OperationCanceledException) { Assert(devices.Opened.Count == 0, "Cancelled worker opened a device"); }
    }

    private static async Task CheckScanBackoff()
    {
        var devices = new FakeDevices(() => true) { FailScan = true };
        var errors = 0;
        using var session = new LightingSession(devices, () => true, _ => errors++);
        await session.TickAsync(LightingSettings.Default, 0, default);
        await session.TickAsync(LightingSettings.Default, 0.1, default);
        Assert(devices.ScanCount == 1 && errors == 1, "Failed discovery retried every animation frame");
        devices.FailScan = false;
        await session.TickAsync(LightingSettings.Default, 1.1, default);
        Assert(session.DeviceCount == 1 && session.WriteCount == 1, "Discovery did not recover");
    }

    private static async Task CheckWriteTimeout()
    {
        var devices = new FakeDevices(() => true);
        devices.OnOpen = () => devices.Opened.Last().Stall = true;
        var errors = 0;
        using var session = new LightingSession(devices, () => true, _ => errors++);
        await session.TickAsync(LightingSettings.Default, 0, default);
        Assert(session.WriteCount == 0 && session.DeviceCount == 0 && errors == 1 && devices.Opened.Single().Disposed, "Stalled HID write not cancelled and closed");
    }

    private static void Probe()
    {
        var devices = new HidDevices();
        var count = 0;
        foreach (var path in devices.GetPaths())
        {
            using var device = devices.Open(path);
            if (device == null) continue;
            count++;
            Console.WriteLine($"USB DualSense interface {count}: output report length {device.ReportLength}; shared handle opened and closed, zero reports written.");
        }
        Console.WriteLine($"Read-only HID probe complete: {count} supported USB controller(s).");
    }

    private sealed class FakeDevices(Func<bool> foreground) : ILightingDevices
    {
        public string[] Paths = ["controller"];
        public readonly List<FakeDevice> Opened = [];
        public Action? OnOpen;
        public int ScanCount;
        public bool FailScan;
        public IReadOnlyCollection<string> GetPaths()
        {
            ScanCount++;
            if (FailScan) throw new IOException("Discovery failed");
            return Paths;
        }
        public ILightingDevice Open(string path)
        {
            var device = new FakeDevice(foreground);
            Opened.Add(device);
            OnOpen?.Invoke();
            return device;
        }
    }

    private sealed class FakeDevice(Func<bool> foreground) : ILightingDevice
    {
        public int ReportLength => 63;
        public bool Disposed, Fail, Stall, AllowBackground;
        public readonly List<byte[]> Reports = [];
        public Action? OnWrite;
        public async Task WriteAsync(byte[] report, CancellationToken cancellationToken)
        {
            Assert((foreground() || AllowBackground) && !Disposed, "Wrote while background or disposed");
            if (Fail) throw new IOException("Controller unplugged");
            if (Stall) await Task.Delay(Timeout.Infinite, cancellationToken);
            Reports.Add((byte[])report.Clone());
            OnWrite?.Invoke();
        }
        public void Dispose() => Disposed = true;
    }
}
