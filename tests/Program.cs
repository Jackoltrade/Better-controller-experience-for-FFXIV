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
        await CheckOwnership();
        await CheckSolidRefresh();
        await CheckFocusRace();
        await CheckMultipleControllers();
        await CheckReconnectAndFailure();
        await CheckScanBackoff();
        await CheckWriteTimeout();
        await CheckCancellation();
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
        public bool Disposed, Fail, Stall;
        public Action? OnWrite;
        public async Task WriteAsync(byte[] report, CancellationToken cancellationToken)
        {
            Assert(foreground() && !Disposed, "Wrote while background or disposed");
            if (Fail) throw new IOException("Controller unplugged");
            if (Stall) await Task.Delay(Timeout.Infinite, cancellationToken);
            OnWrite?.Invoke();
        }
        public void Dispose() => Disposed = true;
    }
}
