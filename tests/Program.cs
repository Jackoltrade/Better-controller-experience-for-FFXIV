// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        CheckReports();
        CheckEffects();
        CheckRgbAnimations();
        CheckPlayerAnimations();
        CheckControllerPreview();
        CheckSteamProfiles();
        await CheckOwnership();
        await CheckSolidRefresh();
        await CheckFocusRace();
        await CheckMultipleControllers();
        await CheckReconnectAndFailure();
        await CheckScanBackoff();
        await CheckWriteTimeout();
        await CheckCancellation();
        await CheckSteamHandoff();
        await CheckGlobalRelease();
        await CheckHandoffRegainFocus();
        await CheckHandoffFailure();
        await CheckAnimatedIndicatorSession();
        await CheckAnimatedRgbSessions();
        Console.WriteLine($"Passed {checks:N0} lighting checks: native report preservation, background guard, effects, focus transitions, reconnect and cancellation.");
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

        foreach (var effect in Enum.GetValues<LightingEffect>())
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
        foreach (var effect in Enum.GetValues<LightingEffect>().Where(e => e != LightingEffect.Solid))
        {
            var foreground = true;
            var devices = new FakeDevices(() => foreground);
            devices.OnOpen = () => devices.Opened.Last().AllowBackground = true;
            using var session = new LightingSession(devices, () => foreground, _ => throw new Exception($"Unexpected {effect} failure"),
                (_, _) => new LightingFrame(0, 0, 0, 0));
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
            Assert(session.HandoffCount == 1 && session.DeviceCount == 0 && devices.Opened.Last().Disposed &&
                devices.Opened.Last().Reports.Last().Skip(44).Take(4).All(b => b == 0), $"RGB effect did not turn off and release on focus loss: {effect}");
            await session.TickAsync(settings, 1.5, default);
            Assert(session.WriteCount == 2 && session.HandoffCount == 1, $"RGB effect continued writing in background: {effect}");
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

    private static async Task CheckOwnership()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground);
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
        using var session = new LightingSession(devices, () => foreground, _ => throw new Exception("Unexpected animation output failure"),
            (device, _) => { ((FakeDevice)device).AllowBackground = true; return new LightingFrame(0, 0, 0, 0); });
        var settings = LightingSettings.Default with { PlayerLights = 27, PlayerAnimation = PlayerIndicatorAnimation.Chase, PlayerCycleSeconds = 2 };
        await session.TickAsync(settings, 0, default);
        await session.TickAsync(settings, 0.51, default);
        await session.TickAsync(settings, 1.01, default);
        Assert(session.WriteCount == 3 && devices.Opened.Single().Reports.Select(r => r[44]).SequenceEqual(new byte[] { 33, 34, 40 }), "Indicator animation used the excluded center or failed with solid RGB");
        foreground = false;
        await session.TickAsync(settings, 1.1, default);
        Assert(session.HandoffCount == 1 && devices.Opened.Single().Reports.Last().Skip(44).Take(4).All(b => b == 0) && devices.Opened.Single().Disposed, "Animated lighting did not turn off and release control");
        await session.TickAsync(settings, 1.5, default);
        Assert(session.HandoffCount == 1 && session.WriteCount == 3, "Player animation kept writing in the background");
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

    private static string SteamText(byte red = 149, byte green = 255, byte blue = 0) =>
        $"\"color_red\" \"{red}\"\n\"color_green\" \"{green}\"\n\"color_blue\" \"{blue}\"\n" +
        "\"guide_brightness\" \"1\"\n\"color_saturation\" \"1\"\n\"player_slot_led\" \"0\"";

    private static void CheckSteamProfiles()
    {
        Assert(SteamLightingProfiles.Parse(SteamText(), 1) == new LightingFrame(149, 255, 0, 0), "Steam saved RGB mismatch");
        var desaturated = SteamText().Replace("\"color_saturation\" \"1\"", "\"color_saturation\" \"0\"")
            .Replace("\"guide_brightness\" \"1\"", "\"guide_brightness\" \"0.5\"");
        Assert(SteamLightingProfiles.Parse(desaturated, 1) == new LightingFrame(128, 128, 128, 0), "Steam saturation/brightness ignored");
        var multiple = SteamText().Replace("\"player_slot_led\" \"0\"", "\"player_slot_led\" \"1\"");
        Assert(SteamLightingProfiles.Parse(multiple, 1).PlayerLights == 0 && SteamLightingProfiles.Parse(multiple, 2).PlayerLights == 4, "Steam multi-controller player indicator mismatch");
        Assert(SteamLightingProfiles.Parse(SteamText().Replace("\"player_slot_led\" \"0\"", "\"player_slot_led\" \"2\""), 1).PlayerLights == 4, "Steam always-on player indicator mismatch");
        foreach (var invalid in new[] {
            SteamText().Replace("\"color_red\" \"149\"", "\"color_red\" \"256\""),
            SteamText().Replace("\"guide_brightness\" \"1\"", "\"guide_brightness\" \"NaN\""),
            SteamText().Replace("\"color_saturation\" \"1\"", "\"color_saturation\" \"-1\""),
            SteamText().Replace("\"player_slot_led\" \"0\"", "\"player_slot_led\" \"1.5\""),
            "\"color_red\" \"149\"" })
        {
            try { SteamLightingProfiles.Parse(invalid, 1); Assert(false, "Invalid Steam profile accepted"); }
            catch (InvalidDataException) { checks++; }
        }
        var root = Path.Combine(Path.GetTempPath(), "DualSenseLightingChecks-" + Guid.NewGuid().ToString("N"));
        string Profile(string account) => Path.Combine(root, "steamapps", "common", "Steam Controller Configs", account, "config", "preferences_DS4cb99b39727d.vdf");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Profile("1"))!);
            File.WriteAllText(Profile("1"), SteamText());
            var profiles = new SteamLightingProfiles(root, () => null);
            Assert(profiles.Read("4C:B9:9B:39:72:7D", 1) == new LightingFrame(149, 255, 0, 0), "Did not match controller serial");
            File.WriteAllText(Profile("1"), SteamText(0, 30, 255));
            Assert(profiles.Read("4cb99b39727d", 1) == new LightingFrame(0, 30, 255, 0), "Restored stale Steam settings");
            Directory.CreateDirectory(Path.GetDirectoryName(Profile("2"))!);
            File.WriteAllText(Profile("2"), SteamText(200, 0, 20));
            Assert(new SteamLightingProfiles(root, () => "2").Read("4cb99b39727d", 1) == new LightingFrame(200, 0, 20, 0), "Steam active-account selection failed");
            try { profiles.Read("4cb99b39727d", 1); Assert(false, "Ambiguous Steam account accepted"); }
            catch (InvalidDataException) { checks++; }
            try { profiles.Read("000000000000", 1); Assert(false, "Unknown controller identity accepted"); }
            catch (InvalidDataException) { checks++; }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task CheckSteamHandoff()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground);
        var profileReads = 0;
        using var session = new LightingSession(devices, () => foreground, _ => throw new Exception("Unexpected handoff error"),
            (device, count) => { profileReads++; ((FakeDevice)device).AllowBackground = true; return new LightingFrame(149, 255, 0, 0); });
        await session.TickAsync(LightingSettings.Default with { Effect = LightingEffect.Rainbow }, 0, default);
        Assert(devices.Opened[0].Reports.Last()[45] == 255, "Rainbow was not red before Alt-Tab");
        foreground = false;
        await session.TickAsync(LightingSettings.Default, 0.1, default);
        var restore = devices.Opened[0].Reports.Last();
        Assert(session.HandoffCount == 1 && restore[44] == 0 && restore[45] == 149 && restore[46] == 255 && restore[47] == 0, "Alt-Tab froze the last rainbow frame instead of restoring Steam");
        Assert(restore[1] == 0 && restore[2] == 0x14 && restore.Skip(3).Take(36).All(b => b == 0), "Steam handoff enabled native haptic/audio/trigger changes");
        for (var i = 1; i <= 100; i++) await session.TickAsync(LightingSettings.Default, i, default);
        Assert(session.HandoffCount == 1 && profileReads == 1 && devices.Opened[0].Reports.Count == 2 && devices.Opened.All(d => d.Disposed), "Steam handoff kept writing in the background");
        foreground = true;
        await session.TickAsync(LightingSettings.Default, 101, default);
        Assert(session.WriteCount == 2 && devices.Opened.Last().Reports.Last()[45] == 119, "FFXIV lighting did not resume after handoff");
        await session.TickAsync(LightingSettings.Default with { Enabled = false }, 102, default);
        Assert(session.HandoffCount == 2 && session.DeviceCount == 0, "Disabling custom lighting did not hand back to Steam");
    }

    private static async Task CheckGlobalRelease()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground);
        devices.OnOpen = () => devices.Opened.Last().AllowBackground = true;
        using var session = new LightingSession(devices, () => foreground,
            _ => throw new Exception("Unexpected global release error"),
            (_, _) => new LightingFrame(0, 0, 0, 0));
        var settings = LightingSettings.Default with { Effect = LightingEffect.Rainbow, PlayerLights = 31 };
        await session.TickAsync(settings, 0, default);
        foreground = false;
        await session.TickAsync(settings, 0.1, default);
        var device = devices.Opened.Single();
        var release = device.Reports.Last();
        Assert(session.HandoffCount == 1 && device.Disposed && session.DeviceCount == 0,
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
        Assert(device.Reports.Count == 2 && devices.Opened.Count == 1 && session.HandoffCount == 1,
            "Background updates or reconnect interfered with another lighting app");
        foreground = true;
        await session.TickAsync(settings, 56, default);
        Assert(session.WriteCount == 2 && devices.Opened.Last().Reports.Last()[45] == 255,
            "Returning to FFXIV did not resume the saved custom effect");
        await session.TickAsync(settings with { Enabled = false }, 57, default);
        Assert(session.HandoffCount == 2 && devices.Opened.Last().Disposed,
            "Disabling lighting did not perform the same automatic release");
        await session.TickAsync(settings, 64, default);
        await session.ReleaseAsync(true, default);
        await session.ReleaseAsync(true, default);
        Assert(session.HandoffCount == 3 && devices.Opened.Last().Disposed,
            "Unloading failed to release lighting once or repeated the release");
    }

    private static async Task CheckHandoffRegainFocus()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground);
        using var session = new LightingSession(devices, () => foreground, _ => { }, (_, _) =>
        { foreground = true; return new LightingFrame(149, 255, 0, 0); });
        await session.TickAsync(LightingSettings.Default, 0, default);
        foreground = false;
        await session.TickAsync(LightingSettings.Default, 0.1, default);
        Assert(session.HandoffCount == 0 && devices.Opened.Single().Reports.Count == 1, "Slow Steam profile read restored over regained foreground");
        await session.TickAsync(LightingSettings.Default, 0.2, default);
        Assert(session.WriteCount == 2, "Foreground did not recover after a skipped restore");
    }

    private static async Task CheckHandoffFailure()
    {
        var foreground = true;
        var devices = new FakeDevices(() => foreground);
        var errors = 0;
        using var session = new LightingSession(devices, () => foreground, _ => errors++, (_, _) => throw new InvalidDataException("No Steam profile"));
        await session.TickAsync(LightingSettings.Default, 0, default);
        foreground = false;
        for (var i = 1; i <= 20; i++) await session.TickAsync(LightingSettings.Default, i, default);
        Assert(errors == 1 && session.HandoffCount == 0 && devices.Opened.Single().Disposed, "Failed restore kept a handle or retried over another app");
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
        devices.OnOpen = () => devices.Opened.Last().OnWrite = () => foreground = false;
        using var session = new LightingSession(devices, () => foreground, _ => { });
        await session.TickAsync(LightingSettings.Default, 0, default);
        Assert(session.WriteCount == 1 && session.DeviceCount == 0 && devices.Opened.All(d => d.Disposed), "Focus lost between controllers still sent another write");
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
        var profiles = new SteamLightingProfiles(SteamInstallation.GetRoot(), SteamInstallation.GetActiveAccount);
        var count = 0;
        foreach (var path in devices.GetPaths())
        {
            using var device = devices.Open(path);
            if (device == null) continue;
            count++;
            Console.WriteLine($"USB DualSense interface {count}: output report length {device.ReportLength}; shared handle opened and closed, zero reports written.");
            Console.WriteLine($"Matched Steam handoff: {profiles.Read(device.Serial, 1)} (read only).");
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
        public string? Serial => "4cb99b39727d";
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
