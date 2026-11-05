// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DualSenseLighting;
using DualSenseLighting.Core;

internal static class BluetoothChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        CheckReports(check);
        CheckSelection(check);
        CheckFirmware(check);
        await CheckSession(check);
        await CheckImprovedSession(check);
        await CheckMotorBalance(check);
        await CheckRumbleOnly(check);
        await CheckFailures(check);
        await CheckCancellation(check);
    }

    private static void CheckReports(Action<bool, string> check)
    {
        var frame = new LightingFrame(11, 222, 33, 27, true);
        var report = DualSenseReport.Create(78, frame, null, 5);
        check(report[0] == 0x31 && report[1] == 0x50 && report[2] == 0x10 &&
            report[4] == 0x14 && report[41] == 3 && report[44] == 2 && report[46] == 0x3b &&
            report[47] == 11 && report[48] == 222 && report[49] == 33,
            "Bluetooth light/indicator report does not match the standard common-block layout");
        // Independent fixture: Python zlib.crc32(bytes([0xa2]) + report[:74]).
        check(BinaryPrimitives.ReadUInt32LittleEndian(report.AsSpan(74)) == 0x6f389813 &&
            DualSenseReport.HasValidChecksum(report), "Bluetooth output CRC differs from the independent vector");
        check(report[3] == 0 && report.AsSpan(5, 36).ToArray().All(b => b == 0),
            "Bluetooth lighting enabled rumble, speaker, microphone or adaptive triggers");
        report = DualSenseReport.Create(78, frame, new(0xab00, 0x1200), 5);
        check(report[3] == 3 && report[5] == 0x12 && report[6] == 0xab &&
            BinaryPrimitives.ReadUInt32LittleEndian(report.AsSpan(74)) == 0xf10551ca,
            "Bluetooth compatible rumble layout or independent combined-output CRC is wrong");
        var rumble = DualSenseReport.Create(78, null, new(65535, 32768), 31);
        check(rumble[1] == 0xf0 && rumble[3] == 3 && rumble[4] == 0 && rumble[5] == 128 && rumble[6] == 255 &&
            rumble.AsSpan(7, 67).ToArray().All(b => b == 0),
            "Rumble-only Bluetooth output changed lights, triggers, microphone or speaker state");
        try { DualSenseReport.Create(63, null, new(), 0); check(false, "USB accepted generated compatible rumble"); }
        catch (ArgumentException) { }

        foreach (var offset in new[] { 2, 3 })
        for (var mask = 0; mask <= 255; mask++)
        {
            var original = new byte[78]; new Random(mask + offset).NextBytes(original);
            original[0] = 0x31; original[1] = offset == 2 ? (byte)2 : (byte)0x90;
            if (offset == 3) original[2] = 0x10;
            original[offset + 1] = original[offset + 38] = (byte)mask;
            DualSenseReport.SetChecksum(original);
            foreach (var foreground in new[] { false, true })
            {
                var output = (byte[])original.Clone();
                var action = DualSenseReport.Apply(output, foreground, true, frame);
                check(action == (foreground ? ReportAction.CustomLighting : ReportAction.BackgroundGuard) &&
                    DualSenseReport.HasValidChecksum(output), "Guard did not accept/checksum a known Bluetooth layout");
                for (var index = 0; index < 74; index++)
                {
                    var changed = index == offset + 1 || index == offset + 38 ||
                        foreground && index >= offset + 41 && index <= offset + 46;
                    if (!changed) check(output[index] == original[index],
                        "Bluetooth lighting guard modified a native rumble/audio/trigger/header field");
                }
                check(output[offset + 1] == (foreground ? (byte)((mask & ~8) | 0x14) : (byte)(mask & ~0x1c)) &&
                    output[offset + 38] == (foreground ? (byte)(mask | 3) : (byte)(mask & ~3)),
                    "Bluetooth lighting validity modified another feature's flags");
            }
            var disabled = (byte[])original.Clone();
            check(DualSenseReport.Apply(disabled, true, false, frame) == ReportAction.Unchanged &&
                disabled.SequenceEqual(original), "Disabled Bluetooth lighting modified native output");
            original[25] ^= 1;
            var corrupt = (byte[])original.Clone();
            check(DualSenseReport.Apply(corrupt, true, true, frame) == ReportAction.Unchanged && corrupt.SequenceEqual(original),
                "Guard rewrote a Bluetooth report with an invalid incoming checksum");
        }
        foreach (var length in new[] { 77, 79, 142, 547 })
        {
            var unknown = new byte[length]; unknown[0] = 0x31;
            check(DualSenseReport.Apply(unknown, false, true, frame) == ReportAction.Unchanged,
                "Lighting guard intercepted a foreign Bluetooth audio/report layout");
        }
        check(DualSenseReport.ReportLength(64, 48) == 48 && DualSenseReport.ReportLength(64, 63) == 63 &&
            DualSenseReport.ReportLength(78, 78) == 78 && DualSenseReport.ReportLength(78, 547) == 78 &&
            DualSenseReport.ReportLength(64, 78) == 0 && DualSenseReport.ReportLength(78, 63) == 0 &&
            DualSenseReport.ReportLength(0, 78) == 0 && DualSenseReport.ReportLength(78, 2048) == 0,
            "HID descriptor detection confused USB/Bluetooth or rejected larger Bluetooth descriptor reports");
    }

    private static void CheckSelection(Action<bool, string> check)
    {
        foreach (var usb in new[] { false, true })
        foreach (var bluetooth in new[] { false, true })
        {
            check(HapticOutputSelection.Resolve(HapticOutputMode.Automatic, usb, bluetooth) ==
                (usb ? HapticOutputMode.DualSense : bluetooth ? HapticOutputMode.Bluetooth : HapticOutputMode.XInput),
                "Automatic backend priority is not USB DualSense, Bluetooth rumble, then XInput");
            foreach (var manual in new[] { HapticOutputMode.DualSense, HapticOutputMode.Bluetooth, HapticOutputMode.XInput })
                check(HapticOutputSelection.Resolve(manual, usb, bluetooth) == manual,
                    "Backend availability silently replaced a manual output choice");
        }
        var config = new Configuration { HapticOutput = HapticOutputMode.Bluetooth };
        check(JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!.HapticsSnapshot().OutputMode == HapticOutputMode.Bluetooth,
            "Bluetooth output did not persist through configuration reload");
        config.ResetHapticsToDefaults();
        check(config.HapticOutput == HapticOutputMode.Automatic && (int)HapticOutputMode.XInput == 2,
            "New output mode changed existing saved enum values or reset behavior");
    }

    private sealed class Devices : ILightingDevices
    {
        public Dictionary<string, int> Paths = new() { ["bt"] = 78 };
        public readonly List<Device> Opened = new();
        public bool Improved;
        public IReadOnlyCollection<string> GetPaths() => Paths.Keys;
        public ILightingDevice Open(string path)
        {
            var device = new Device(path, Paths[path]) { ImprovedRumble = Improved }; Opened.Add(device); return device;
        }
    }

    private static void CheckFirmware(Action<bool, string> check)
    {
        foreach (var (version, crc, supported) in new[]
        {
            (0x0214, 0xc7dc1647u, false), (0x0215, 0x69b487d6u, true), (0x0300, 0x4c31bfb6u, true),
        })
        {
            var feature = new byte[64]; feature[0] = 0x20;
            BinaryPrimitives.WriteUInt16LittleEndian(feature.AsSpan(44), (ushort)version);
            BinaryPrimitives.WriteUInt32LittleEndian(feature.AsSpan(60), crc); // Independent Python zlib / seed 0xA3.
            check(DualSenseReport.SupportsImprovedRumble(0x0ce6, feature) == supported,
                "Improved rumble used the wrong feature-version threshold or feature CRC seed");
            feature[24] ^= 1;
            check(!DualSenseReport.SupportsImprovedRumble(0x0ce6, feature), "Corrupt firmware metadata enabled vibration v2");
        }
        check(DualSenseReport.SupportsImprovedRumble(0x0df2, default) &&
            !DualSenseReport.SupportsImprovedRumble(0x0ce6, default) &&
            !DualSenseReport.SupportsImprovedRumble(0x05c4, new byte[64]),
            "Firmware fallback did not distinguish Edge, unknown regular firmware and another controller");
        foreach (var strength in new ushort[] { 0, 256, 16384, 32768, 65535 })
        {
            var output = DualSenseReport.Create(78, null, new(strength, strength), 2, true);
            check(output[3] == 2 && output[41] == 4 && output[5] == strength >> 8 && output[6] == strength >> 8 &&
                output[4] == 0 && DualSenseReport.HasValidChecksum(output),
                "Improved compatible-rumble packets collapsed intensity or changed another feature");
        }
    }

    private static async Task CheckImprovedSession(Action<bool, string> check)
    {
        var devices = new Devices { Improved = true };
        using var session = new LightingSession(devices, () => true, e => throw e);
        session.Discover(true, true, 0);
        check(session.BluetoothImprovedRumble, "Firmware-supported vibration v2 was not published");
        session.StartStrengthTest(0);
        session.SetVibration(0, new(16384, 32768));
        await session.TickAsync(LightingSettings.Default, 0, default);
        var output = devices.Opened[0].Reports[^1];
        check(output[3] == 2 && output[41] == 7 && output[5] == 128 && output[6] == 64 &&
            session.BluetoothMotorCommand == ((byte)64, (byte)128),
            "The actual improved Bluetooth write or diagnostic motor values lost their amplitude");
        session.SetVibration(0, new(32768, 32768));
        await session.TickAsync(LightingSettings.Default, 1, default);
        check(session.BluetoothStrengthTest == ((byte)64, (byte)128, 2),
            "The strength-test diagnostic did not retain the actual serialized command range");
        check(session.BluetoothTestCommands == ((byte)64, (byte)128, (byte)128, (byte)128),
            "Strength-test diagnostics did not retain both actual motor commands per pulse");
        session.SetVibration(0, default);
        await session.TickAsync(LightingSettings.Default, 1.6, default);
        check(session.BluetoothMotorCommand == default &&
            session.BluetoothTestCommands == ((byte)64, (byte)128, (byte)128, (byte)128),
            "The final stop command erased the useful strength-test pulse readings");

        session.StartStrengthTest(2);
        check(session.BluetoothStrengthTest.Writes == 0 && session.BluetoothTestCommands == default,
            "A new motor test retained the previous test's commands");
        session.SetVibration(0, new(32768, 0));
        await session.TickAsync(LightingSettings.Default, 2, default);
        session.SetVibration(0, default);
        await session.TickAsync(LightingSettings.Default, 2.7, default);
        session.SetVibration(0, new(0, 32768));
        await session.TickAsync(LightingSettings.Default, 3, default);
        session.SetVibration(0, default);
        await session.TickAsync(LightingSettings.Default, 3.6, default);
        check(session.BluetoothTestCommands == ((byte)128, (byte)0, (byte)0, (byte)128) &&
            session.BluetoothStrengthTest == ((byte)128, (byte)128, 2),
            "Isolated right-motor diagnostics were skipped or hidden by stop commands");
        session.SetVibration(0, new(32768, 32768));
        await session.TickAsync(LightingSettings.Default, 4, default);
        await session.ReleaseAsync(true, default);
        output = devices.Opened[0].Reports[^1];
        check(output[3] == 2 && output[41] == 7 && output[5] == 0 && output[6] == 0 &&
            !session.BluetoothImprovedRumble && session.BluetoothMotorCommand == default && session.BluetoothTestCommands == default,
            "Improved-mode release lost its mode bits or retained stale status/motors");
    }

    private static async Task CheckMotorBalance(Action<bool, string> check)
    {
        foreach (var (balance, expectedLeft, expectedRight) in new[] { (0f, 128, 128), (0.5f, 64, 128), (-0.5f, 128, 64) })
        {
            var devices = new Devices { Improved = true };
            using var transport = new LightingSession(devices, () => true, e => throw e);
            using var session = new RumbleSession(transport, () => false);
            transport.Discover(false, true, 0);
            session.Select(HapticOutputMode.XInput, 0, true, 0);
            var signal = new SfxLoudness(); signal.SetActive(true);
            var settings = new HapticSettings(Enabled: true, ScreenEffects: true, RumbleStrength: 0.5f,
                BluetoothMotorBalance: balance) { BluetoothRumble = true };
            var lighting = LightingSettings.Default with { Enabled = false };
            bool Matches(int left, int right) => devices.Opened[0].Reports[^1] is var bytes &&
                bytes[6] == left && bytes[5] == right && bytes[4] == 0 && DualSenseReport.HasValidChecksum(bytes);
            signal.Observe(new float[] { 1 });
            session.Tick(signal, settings, true, 0, stereo: (1, 1));
            await transport.TickAsync(lighting, 0, default);
            check(Matches(expectedLeft, expectedRight), "Bluetooth combat balance did not reach the actual motor bytes");

            session.Tick(signal, settings, true, 0.2, screenLevel: 0.5f, stereo: (0, 0));
            await transport.TickAsync(lighting, 0.2, default);
            check(Matches(expectedLeft, expectedRight), "Bluetooth screen output bypassed the motor calibration");

            transport.StartStrengthTest(1);
            session.RequestStrengthTest(1, true);
            session.Tick(signal, settings, true, 1, screenLevel: 1);
            await transport.TickAsync(lighting, 1, default);
            check(Matches(expectedLeft, 0), "Left motor test ignored calibration or allowed screen feedback");
            session.Tick(signal, settings, true, 1.7, screenLevel: 1);
            await transport.TickAsync(lighting, 1.7, default);
            check(Matches(0, 0), "Calibration left a motor running during the test gap");
            session.Tick(signal, settings, true, 2, screenLevel: 1);
            await transport.TickAsync(lighting, 2, default);
            check(Matches(0, expectedRight) && transport.BluetoothTestCommands ==
                ((byte)expectedLeft, (byte)0, (byte)0, (byte)expectedRight),
                "Right motor test or retained diagnostics ignored the calibrated command");
            session.Tick(signal, settings, false, 2.1);
            await transport.TickAsync(lighting, 2.1, default);
            check(Matches(0, 0), "Motor balance interfered with the zero/disable command");

            signal.Observe(new float[] { 1 });
            session.Tick(signal, settings, true, 3, screenLevel: 1, stereo: (1, 1));
            await transport.TickAsync(lighting, 3, default);
            check(Matches(balance > 0 ? 128 : 255, balance < 0 ? 128 : 255),
                "Overlapping screen/combat saturation defeated the Bluetooth motor balance");
            session.Tick(signal, settings, false, 3.1);
            await transport.TickAsync(lighting, 3.1, default);
            check(Matches(0, 0), "A saturated calibrated mix did not stop on disable");
        }
    }

    private sealed class Device(string path, int length) : ILightingDevice
    {
        public string Path => path;
        public int ReportLength => length;
        public bool Disposed, Fail;
        public bool ImprovedRumble { get; set; }
        public Action? AfterWrite;
        public readonly List<byte[]> Reports = new();
        public Task WriteAsync(byte[] report, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Disposed || Fail) throw new IOException("Disconnected test device");
            Reports.Add((byte[])report.Clone());
            AfterWrite?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        public void Dispose() => Disposed = true;
    }

    private static async Task CheckSession(Action<bool, string> check)
    {
        var foreground = true;
        var devices = new Devices(); devices.Paths.Add("usb", 63); devices.Paths.Add("bt2", 78);
        using var session = new LightingSession(devices, () => foreground, e => throw e);
        var settings = LightingSettings.Default with { Effect = LightingEffect.Rainbow, PlayerLights = 27 };
        session.Discover(true, true, 0);
        check(session.HasUsb && session.HasBluetooth && session.BluetoothCount == 2 &&
            session.IsConnected(0) && !session.IsConnected(1), "Discovery did not publish one stable Bluetooth rumble target");
        session.SetVibration(0, new(0xab00, 0x1200));
        for (var index = 0; index < 33; index++)
        {
            await session.TickAsync(settings, index * 0.033, default);
            var actual = devices.Opened.Single(d => d.Path == "bt").Reports[^1];
            var preview = LightingEffects.Render(settings, index * 0.033);
            check(actual[1] == ((index & 15) << 4) && actual[47] == preview.Red && actual[48] == preview.Green &&
                actual[49] == preview.Blue && (actual[46] & 31) == preview.PlayerLights &&
                actual[5] == 0x12 && actual[6] == 0xab && DualSenseReport.HasValidChecksum(actual),
                "Combined Bluetooth animation, rumble, sequence wrap or preview diverged");
        }
        check(devices.Opened.Single(d => d.Path == "usb").Reports.All(r => r[1] == 0) &&
            devices.Opened.Single(d => d.Path == "bt2").Reports.All(r => r[3] == 0),
            "Basic Bluetooth rumble leaked into USB native haptics or a second Bluetooth controller");
        session.Discover(false, true, 1.1);
        await session.TickAsync(settings with { Enabled = false }, 1.1, default);
        var primary = devices.Opened.Single(d => d.Path == "bt");
        check(primary.Reports[^1].AsSpan(47, 3).ToArray().All(b => b == 0) && primary.Reports[^1][46] == 0 &&
            primary.Reports[^1][6] == 0xab && session.HasBluetooth,
            "Disabling lighting stopped rumble, retained LEDs, or closed its transport");
        session.SetVibration(0, new(10000, 20000));
        await session.TickAsync(settings with { Enabled = false }, 1.2, default);
        check(primary.Reports[^1][4] == 0 && primary.Reports[^1][41] == 0 && primary.Reports[^1][6] > 0,
            "Rumble-only update reclaimed lighting after the lighting switch was disabled");
        foreground = false;
        var before = primary.Reports.Count;
        await session.TickAsync(settings, 1.3, default);
        await session.TickAsync(settings, 1.4, default);
        check(primary.Reports.Count == before + 1 && primary.Reports[^1][3] == 3 &&
            primary.Reports[^1][5] == 0 && primary.Reports[^1][6] == 0 && primary.Disposed &&
            devices.Opened.All(d => d.Disposed) && session.DeviceCount == 0,
            "Bluetooth background release did not stop rumble once and close every shared handle");
        foreground = true;
        session.Discover(true, true, 1.5);
        await session.TickAsync(settings, 1.5, default);
        var reconnected = devices.Opened.Last(d => d.Path == "bt");
        check(reconnected.Reports[^1][3] == 0 && reconnected.Reports[^1][5] == 0 && reconnected.Reports[^1][6] == 0 &&
            reconnected.Reports[^1][1] == 0 && DualSenseReport.HasValidChecksum(reconnected.Reports[^1]),
            "Focus return replayed stale motors or retained a prior connection sequence");
    }

    private static async Task CheckRumbleOnly(Action<bool, string> check)
    {
        var foreground = true;
        var devices = new Devices();
        using var hid = new LightingSession(devices, () => foreground, e => throw e);
        using var rumble = new RumbleSession(hid, () => false);
        var signal = new SfxLoudness(); signal.SetActive(true);
        var settings = new HapticSettings(Enabled: true, DistanceFalloff: false) { BluetoothRumble = true };
        var lights = LightingSettings.Default with { Enabled = false };
        hid.Discover(false, true, 0);
        rumble.Select(HapticOutputMode.XInput, 0, true, 0);
        signal.Observe(new float[] { 0.5f }); rumble.Tick(signal, settings, true, 0);
        await hid.TickAsync(lights, 0, default);
        var device = devices.Opened.Single(); var first = device.Reports.Single();
        check(first[3] == 3 && first[4] == 0 && first[5] == 64 && first[6] == 9 && first[41] == 0,
            "The default +85% balance or SFX rumble envelope did not reach Bluetooth independently of lighting");
        signal.Observe(new float[] { 0.5f }); rumble.Tick(signal, settings, true, 0.06);
        await hid.TickAsync(lights, 0.06, default);
        check(device.Reports.Count == 1, "An unchanged impact sent duplicate Bluetooth commands");
        signal.Observe(new float[] { 0.5f }); rumble.Tick(signal, settings, true, 0.15);
        await hid.TickAsync(lights, 0.15, default);
        check(device.Reports.Count == 2 && device.Reports[^1][3] == 3 && device.Reports[^1][5] == 0 && device.Reports[^1][6] == 0,
            "A sustained SFX did not stop at the configured pulse deadline");
        signal.Observe(new float[] { 0.5f }); rumble.Tick(signal, settings, true, 0.5);
        await hid.TickAsync(lights, 0.5, default);
        check(device.Reports.Count == 2, "A steady SFX retriggered rumble without another impact");
        // The transport still supports keepalive for a sustained command; pulse shaping belongs to RumbleSession.
        hid.SetVibration(0, new(32768, 32768));
        await hid.TickAsync(lights, 1, default);
        await hid.TickAsync(lights, 1.5, default);
        check(device.Reports.Count == 3, "Unchanged Bluetooth motor state was sent every worker tick");
        await hid.TickAsync(lights, 2.1, default);
        check(device.Reports.Count == 4, "Sustained Bluetooth motor state lost its keepalive");
        hid.SetVibration(0, default); rumble.Release(); await hid.TickAsync(lights, 2.2, default);
        check(device.Reports[^1][3] == 3 && device.Reports[^1][5] == 0 && device.Reports[^1][6] == 0 &&
            !rumble.UsesRumble, "Disabling basic rumble did not send a real motor stop");
        rumble.Select(HapticOutputMode.XInput, 0, true, 3);
        rumble.RequestConfirmation(3);
        foreach (var (time, on) in new[] { (3.0, true), (3.15, false), (3.25, true), (3.35, false) })
        {
            rumble.Tick(signal, settings, true, time); await hid.TickAsync(lights, time, default);
            check((device.Reports[^1][5] != 0) == on && device.Reports[^1][4] == 0,
                "Bluetooth enable confirmation did not preserve its two pulses or changed lighting");
        }
        var generation = hid.BluetoothGeneration;
        devices.Paths.Clear(); hid.Discover(false, true, 4.5);
        check(!hid.HasBluetooth && device.Disposed && hid.BluetoothGeneration != generation,
            "Bluetooth disconnect did not retire the selected connection generation");
        devices.Paths.Add("bt", 78); hid.Discover(false, true, 5.6);
        await hid.TickAsync(lights, 5.6, default);
        check(devices.Opened[^1].Reports.Count == 0 && hid.HasBluetooth,
            "Reconnect replayed a previous Bluetooth rumble command");
    }

    private static async Task CheckFailures(Action<bool, string> check)
    {
        var devices = new Devices(); var errors = 0;
        using var session = new LightingSession(devices, () => true, _ => errors++);
        session.Discover(true, true, 0); session.SetVibration(0, new(30000, 30000));
        await session.TickAsync(LightingSettings.Default, 0, default);
        devices.Opened[0].Fail = true;
        await session.TickAsync(LightingSettings.Default with { Red = 1 }, 0.1, default);
        check(errors == 1 && session.DeviceCount == 0 && !session.IsConnected(0) && devices.Opened[0].Disposed,
            "Bluetooth write failure retained rumble availability or a stale handle");
        session.Discover(true, true, 1); await session.TickAsync(LightingSettings.Default, 1, default);
        check(devices.Opened[^1].Reports[^1][3] == 0 && devices.Opened[^1].Reports[^1][1] == 0,
            "Bluetooth write recovery replayed stale motors or sequence state");
        session.SetVibration(0, new(30000, 40000));
        await session.TickAsync(LightingSettings.Default, 1.1, default);
        var active = devices.Opened[^1]; var before = active.Reports.Count;
        session.Discover(false, false, 1.2);
        await session.TickAsync(LightingSettings.Default with { Enabled = false }, 1.2, default);
        check(active.Reports.Count == before + 1 && active.Disposed && active.Reports[^1][3] == 3 &&
            active.Reports[^1][4] == 0x14 && active.Reports[^1][5] == 0 && active.Reports[^1][6] == 0 &&
            active.Reports[^1].AsSpan(46, 4).ToArray().All(b => b == 0) &&
            DualSenseReport.HasValidChecksum(active.Reports[^1]),
            "Full disable did not clear both owned lighting and rumble in one valid Bluetooth report");
    }

    private static async Task CheckCancellation(Action<bool, string> check)
    {
        foreach (var cancellingStop in new[] { false, true })
        {
            var devices = new Devices();
            using var session = new LightingSession(devices, () => true, e => throw e);
            using var cancelled = new CancellationTokenSource();
            session.Discover(true, true, 0); session.SetVibration(0, new(30000, 40000));
            var device = devices.Opened.Single();
            if (cancellingStop)
            {
                await session.TickAsync(LightingSettings.Default, 0, default);
                session.SetVibration(0, default);
            }
            device.AfterWrite = cancelled.Cancel;
            try
            {
                await session.TickAsync(LightingSettings.Default, 0.1, cancelled.Token);
                check(false, "The Bluetooth delivery/cancellation fixture did not cancel");
            }
            catch (OperationCanceledException) { }
            device.AfterWrite = null;
            await session.ReleaseAsync(true, default);
            var final = device.Reports[^1];
            check(device.Reports.Count == (cancellingStop ? 3 : 2) && final[3] == 3 && final[4] == 0x14 &&
                final[5] == 0 && final[6] == 0 && final.AsSpan(46, 4).ToArray().All(b => b == 0) &&
                DualSenseReport.HasValidChecksum(final) && device.Disposed,
                "Cancelling a possibly delivered Bluetooth start/stop left owned rumble or lights unreleased");
        }
    }
}
