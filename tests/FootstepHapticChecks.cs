// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static unsafe class FootstepHapticChecks
{
    private static int calls;
    private static nint mixedDestination;
    private static float mixedGain, mixedSample;
    private static uint mixedFormat, mixedFrames;

    [UnmanagedCallersOnly]
    private static void Capture(nint destination, float* samples, uint frames, float gain, uint format)
    {
        calls++; mixedDestination = destination; mixedGain = gain; mixedSample = samples[0];
        mixedFormat = format; mixedFrames = frames;
    }

    internal static void Run(Action<bool, string> check)
    {
        foreach (var mode in new[] { HapticOutputMode.Automatic, HapticOutputMode.DualSense,
            HapticOutputMode.XInput, HapticOutputMode.Bluetooth, (HapticOutputMode)999 })
        foreach (var usb in new[] { false, true })
        foreach (var bluetooth in new[] { false, true })
            check(HapticOutputSelection.SupportsFootstepAmplification(mode, usb, bluetooth) ==
                (usb && mode is not (HapticOutputMode.XInput or HapticOutputMode.Bluetooth)),
                "Native footstep availability admitted a missing/wireless/non-native controller or rejected USB DualSense");
        var requested = new HapticSettings(FootstepAmplifier: true);
        check(!new HapticSettings().FootstepAmplifier && !new HapticSettings().NeedsAudio && requested.NeedsAudio,
            "Footstep amplification must be independently opt-in");
        check(requested.FootstepMultiplier == 1.5f && !requested.Enabled, "Footsteps unexpectedly enabled combat feedback or changed their default gain");
        check((requested with { FootstepMultiplier = float.NaN }).Normalize().FootstepMultiplier == 1.5f &&
            (requested with { FootstepMultiplier = 0 }).Normalize().FootstepMultiplier == 1 &&
            (requested with { FootstepMultiplier = 9 }).Normalize().FootstepMultiplier == 6, "Footstep gain limits are not bounded at 6x");
        check(MathF.Abs(requested.NativeFootstepExtraGain(6, true, 0.4f) - 0.2f) < 0.00001f,
            "150% amplification did not add exactly half the existing native send gain");
        check((requested with { FootstepMultiplier = 1 }).NativeFootstepExtraGain(6, true, 0.4f) == 0 &&
            requested.NativeFootstepExtraGain(6, false, 0.4f) == 0 &&
            requested.NativeFootstepExtraGain(6, true, float.NaN) == 0 &&
            requested.NativeFootstepExtraGain(6, true, -1) == 0 &&
            requested.NativeFootstepExtraGain(6, true, 100) == 0, "Footstep amplification synthesized a route or accepted invalid native gain");
        for (byte bus = 0; bus < 21; bus++)
            if (bus != 6) check(requested.NativeFootstepExtraGain(bus, true, 1) == 0, "Footstep gain included another sound category");

        var table = stackalloc nint[16]; new Span<nint>(table, 16).Clear();
        var mix = (nint)(delegate* unmanaged<nint, float*, uint, float, uint, void>)&Capture;
        table[15] = mix;
        var destination = stackalloc byte[0x160]; var replacement = stackalloc byte[0x160];
        new Span<byte>(destination, 0x160).Clear(); new Span<byte>(replacement, 0x160).Clear();
        *(nint*)destination = *(nint*)replacement = (nint)table;
        *(int*)(destination + 0xE0) = *(int*)(replacement + 0xE0) = 1;
        "Vibration\0"u8.CopyTo(new Span<byte>(destination + 0x13E, 10));
        "Vibration\0"u8.CopyTo(new Span<byte>(replacement + 0x13E, 10));
        var graph = stackalloc nint[15]; new Span<nint>(graph, 15).Clear(); graph[9] = (nint)destination;
        var output = new NativeHapticOutput(graph, mix, (nint)table, (nint)table + 16 * 8);
        GameAudioLayout.SourceVoice voice = default; voice.SendCount = 2;
        var sends = (GameAudioLayout.VoiceSend*)((byte*)&voice + 0x940);
        sends[0].Destination = 0x123456; sends[0].Gain = 0.8f; // Audible speaker route remains untouched.
        sends[1].Destination = (nint)destination; sends[1].Gain = 0.4f;
        var originalVoice = new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice)).ToArray();
        var pcm = Enumerable.Repeat(0.1f, 4096).ToArray();
        calls = 0;
        output.Configure(requested, true, false, true);
        check(!output.Active, "Footstep amplification ran without an unmounted local character");
        output.SetFootstepContext(true, false);
        check(output.Active && output.FootstepsActive && !output.WantsSpatial,
            "Footstep-only mode depends on combat attribution or its spatial settings");
        var frame = output.Prepare(&voice, false, 6, 2);
        check(frame.Route == HapticRoute.NativeFootstep && frame.Destination == (nint)destination &&
            MathF.Abs(frame.Gain + sends[1].Gain - sends[1].Gain * 1.5f) < 0.00001f && !frame.Spatial.Valid,
            "Native footstep amplifier lost the native route, gain or grip pattern");
        fixed (float* samples = pcm)
        {
            output.Mix(frame, samples, 2048, 0);
            check(calls == 1 && mixedDestination == (nint)destination && mixedGain == 0.2f && mixedSample == 0.1f &&
                mixedFrames == 256 && mixedFormat == 0 && output.FootstepBlocks == 1 && output.MixedBlocks == 0,
                "Footstep PCM was not added solely to the native vibration mixer");
            check(new ReadOnlySpan<byte>(&voice, sizeof(GameAudioLayout.SourceVoice)).SequenceEqual(originalVoice) &&
                pcm.All(value => value == 0.1f), "Amplifying footsteps changed native PCM, gain or speaker sends");

            var connected = output.Prepare(&voice, false, 6, 2);
            check(connected.HasOutput, "The disconnect fixture did not queue actual native footstep feedback");
            output.Configure(requested, true, false, true, nativeFootstepSupported: false);
            output.Mix(connected, samples, 2048, 0);
            check(calls == 1 && !output.FootstepsActive && !output.CurrentSettings.FootstepAmplifier &&
                output.Prepare(&voice, false, 6, 2).Destination == 0 && requested.FootstepAmplifier,
                "Disconnecting native DualSense left footsteps active or cleared the saved choice");
            output.Configure(requested, true, false, true, nativeFootstepSupported: true);
            output.Mix(connected, samples, 2048, 0);
            check(calls == 1 && output.FootstepsActive && output.Prepare(&voice, false, 6, 2).HasOutput,
                "Reconnect revived a stale footstep frame or failed to resume the saved amplifier");

            var stronger = requested with { FootstepMultiplier = 6, Spatial = true, DistanceFalloff = true };
            output.Configure(stronger, true, false, true);
            output.Mix(frame, samples, 2048, 0);
            check(calls == 1, "A queued footstep used stale amplifier settings");
            frame = output.Prepare(&voice, false, 6, 2);
            output.Mix(frame, samples, 4096, 1);
            check(calls == 2 && mixedGain == 2 && mixedFormat == 1 && !frame.Spatial.Valid && output.UnpositionedBlocks == 0,
                "6x native footsteps lost the requested gain/format or inherited combat distance/spatial processing");

            sends[1].Destination = 0x654321;
            check(output.Prepare(&voice, false, 6, 2).Destination == 0 && output.Prepare(&voice, true, 6, 0).Destination == 0,
                "Audible footsteps without a native vibration send acquired new haptics");
            sends[1].Destination = (nint)destination;
            foreach (var gain in new[] { 0, -1, float.NaN, float.PositiveInfinity })
            {
                sends[1].Gain = gain;
                check(output.Prepare(&voice, false, 6, 2).Destination == 0, "Invalid or muted native footstep gain was amplified");
            }
            sends[1].Gain = 0.4f;
            for (byte bus = 0; bus < 21; bus++)
                if (bus != 6) check(output.Prepare(&voice, true, bus, 2).Destination == 0,
                    "Native combat, mount/music or speaker sounds were included by the footstep-only amplifier");

            frame = output.Prepare(&voice, false, 6, 2);
            output.SetFootstepContext(true, true);
            output.Mix(frame, samples, 2048, 0);
            check(!output.FootstepsActive && output.Prepare(&voice, false, 6, 2).Destination == 0 && calls == 2,
                "Mounted movement was amplified");
            output.SetFootstepContext(true, false);
            output.Mix(frame, samples, 2048, 0);
            check(calls == 2, "An in-flight footstep survived mounting/dismounting");
            output.Mix(output.Prepare(&voice, false, 6, 2), samples, 2048, 0);
            check(calls == 3, "Footstep amplifier did not resume after dismounting");

            frame = output.Prepare(&voice, false, 6, 2);
            output.Configure(stronger, false, false, true);
            output.Mix(frame, samples, 2048, 0);
            output.Configure(stronger, true, false, true);
            output.Mix(frame, samples, 2048, 0);
            check(calls == 3, "Queued footstep amplification survived an Alt-Tab cycle");
            output.SetFootstepContext(false, false);
            check(!output.FootstepsActive, "Footsteps stayed active after logout");
            output.SetFootstepContext(true, false);

            graph[9] = (nint)replacement; sends[1].Destination = (nint)replacement;
            output.Mix(output.Prepare(&voice, false, 6, 2), samples, 2048, 0);
            check(calls == 4 && mixedDestination == (nint)replacement, "Footstep amplification retained a stale mixer after replugging");
            pcm[0] = float.NaN;
            output.Mix(output.Prepare(&voice, false, 6, 2), samples, 2048, 0);
            check(calls == 4, "Nonfinite footstep PCM reached native vibration processing");
            pcm[0] = 0.1f;
            table[15] = 0;
            check(output.Prepare(&voice, false, 6, 2).Destination == 0, "Footstep amplifier accepted an unsupported mixer");
            table[15] = mix;
            output.Configure(stronger, true, true, false);
            check(!output.Active, "Footstep amplifier ran with an unsupported audio layout");
            output.Configure(stronger with { FootstepMultiplier = 1 }, true, false, true);
            check(!output.Active && output.Prepare(&voice, false, 6, 2).Destination == 0, "100% did not retain exactly native footstep strength");
            output.Configure(new HapticSettings(), true, true, true);
            check(!output.Active, "Disabling both haptics options retained footstep amplification");
        }
        CheckDeviceDiscovery(check);
    }

    private static void CheckDeviceDiscovery(Action<bool, string> check)
    {
        var foreground = true;
        var devices = new DiscoveryDevices();
        using var session = new LightingSession(devices, () => foreground, error => throw error);
        var lightsOff = LightingSettings.Default with { Enabled = false };
        session.Discover(false, true, 0); // Settings-only discovery, without lighting or rumble writes.
        session.TickAsync(lightsOff, 0, default).GetAwaiter().GetResult();
        check(session.HasUsb && !session.HasBluetooth && devices.Reports == 0 && devices.Closed == 0,
            "Settings-only native-device discovery wrote outputs or lost the USB controller with lights off");
        devices.Connected = false;
        session.Discover(false, true, 1);
        check(!session.HasUsb && devices.Closed == 1 && devices.Reports == 0,
            "An unplugged USB device kept footstep availability or received a discovery report");
        devices.Connected = true;
        session.Discover(false, true, 2);
        session.TickAsync(lightsOff, 2, default).GetAwaiter().GetResult();
        check(session.HasUsb && devices.Reports == 0,
            "Replug did not restore settings-only availability without lighting writes");
        foreground = false;
        session.TickAsync(lightsOff, 2.1, default).GetAwaiter().GetResult();
        check(!session.HasUsb && devices.Closed == 2 && devices.Reports == 0,
            "Settings-only device handles or availability survived focus loss");
        foreground = true;
        session.Discover(false, true, 3);
        check(session.HasUsb, "Settings-only discovery failed to resume on focus return");
        session.Discover(false, false, 3.1); // Settings closed with all features disabled.
        session.TickAsync(lightsOff, 3.1, default).GetAwaiter().GetResult();
        check(!session.HasUsb && session.DeviceCount == 0 && devices.Closed == 3 && devices.Reports == 0,
            "Settings-only discovery retained handles after the window and all features were disabled");
    }

    private sealed class DiscoveryDevices : ILightingDevices
    {
        internal bool Connected = true;
        internal int Reports, Closed;
        public IReadOnlyCollection<string> GetPaths() => Connected ? ["usb"] : Array.Empty<string>();
        public ILightingDevice? Open(string path) => new UsbDevice(this);
        private sealed class UsbDevice(DiscoveryDevices owner) : ILightingDevice
        {
            public int ReportLength => 48;
            public Task WriteAsync(byte[] report, CancellationToken cancellationToken)
            { owner.Reports++; return Task.CompletedTask; }
            public void Dispose() => owner.Closed++;
        }
    }

}
