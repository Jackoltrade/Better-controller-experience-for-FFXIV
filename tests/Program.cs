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
        await CheckHandoffRegainFocus();
        await CheckHandoffFailure();
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
