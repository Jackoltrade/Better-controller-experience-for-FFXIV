// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Linq;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

internal static unsafe class HapticEnableCueChecks
{
    private static int calls;
    private static nint lastDestination;
    private static float lastGain;
    private static uint lastFrames, lastFormat;
    private static readonly float[] lastPcm = new float[2048];

    [UnmanagedCallersOnly]
    private static void Capture(nint destination, float* pcm, uint frames, float gain, uint format)
    {
        calls++; lastDestination = destination; lastGain = gain; lastFrames = frames; lastFormat = format;
        new ReadOnlySpan<float>(pcm, 2048).CopyTo(lastPcm);
    }

    internal static void Run(Action<bool, string> check)
    {
        var blocks = (HapticEnableCue.TotalFrames + 255) / 256;
        var cue = new HapticEnableCue(long.MaxValue);
        var pcm = new float[2048];
        var mono = new float[blocks * 256];
        for (var block = 0; block < blocks; block++)
        {
            check(cue.Render(pcm, 1), "Confirmation stopped before both pulses finished");
            for (var frame = 0; frame < 256; frame++)
            {
                mono[block * 256 + frame] = pcm[frame * 8];
                check(pcm[frame * 8] == pcm[frame * 8 + 1], "Confirmation did not balance both grips");
                check(float.IsFinite(pcm[frame * 8]) && MathF.Abs(pcm[frame * 8]) <= 0.25f,
                    "Confirmation exceeded its bounded amplitude");
                for (var channel = 2; channel < 8; channel++)
                    check(pcm[frame * 8 + channel] == 0, "Confirmation included another bed channel");
            }
        }
        check(!cue.Render(pcm, 1) && pcm.All(v => v == 0), "Confirmation repeated or left a nonzero tail");
        var first = mono.AsSpan(0, HapticEnableCue.PulseFrames);
        var second = mono.AsSpan(HapticEnableCue.PulseFrames + HapticEnableCue.GapFrames, HapticEnableCue.PulseFrames);
        check(first.SequenceEqual(second) && SfxLoudness.MeasureRms(first) > 0.1f, "Confirmation pulses differ or are silent");
        check(SfxLoudness.MeasureRms(mono.AsSpan(HapticEnableCue.PulseFrames, HapticEnableCue.GapFrames)) == 0,
            "Confirmation lacks a silent gap");
        check(first[0] == 0 && first[^1] == 0, "Confirmation has an abrupt start or stop");
        var expired = new HapticEnableCue(10);
        check(!expired.Render(pcm, 10) && pcm.All(v => v == 0), "An old queued cue played after its deadline");

        var allocationCue = new HapticEnableCue(long.MaxValue);
        allocationCue.Render(pcm, 1); // Warm the render path before measuring.
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < blocks; i++) allocationCue.Render(pcm, 1);
        check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Confirmation allocated on the audio render path");

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
        var settings = new HapticSettings(Enabled: true, Strength: 0, Spatial: true);
        output.Configure(settings, true, true);
        output.MixEnableCue((nint)destination, true, true);
        check(calls == 0 && !output.HasEnableCue, "Loading/configuring haptics triggered a confirmation");
        output.RequestEnableCue();
        output.MixEnableCue((nint)replacement, true, true);
        check(calls == 0, "Confirmation mixed into a voice outside the current vibration graph");
        var original = new ReadOnlySpan<byte>(destination, 0x160).ToArray();
        for (var i = 0; i < blocks; i++) output.MixEnableCue((nint)destination, true, true);
        check(calls == blocks && lastDestination == (nint)destination && lastGain == 1 && lastFrames == 256 && lastFormat == 0,
            "Confirmation did not use the existing native vibration bus/format");
        check(!output.HasEnableCue && new ReadOnlySpan<byte>(destination, 0x160).SequenceEqual(original),
            "Confirmation changed native sends or did not finish");
        output.Configure(settings with { Strength = 0.8f }, true, true);
        output.MixEnableCue((nint)destination, true, true);
        check(calls == blocks, "A strength change replayed the confirmation");
        foreach (var mode in new[] { "background", "unsupported", "disabled", "attribution" })
        {
            output.Configure(settings, true, true);
            output.RequestEnableCue();
            if (mode == "disabled") output.Configure(settings with { Enabled = false }, true, true);
            if (mode == "attribution") output.Configure(settings, true, false);
            output.MixEnableCue((nint)destination, mode != "background", mode != "unsupported");
            check(!output.HasEnableCue && calls == blocks, "Confirmation survived " + mode);
            output.Configure(settings, true, true);
            output.MixEnableCue((nint)destination, true, true);
            check(calls == blocks, "Confirmation replayed after " + mode);
        }
        output.RequestEnableCue();
        graph[9] = (nint)replacement;
        output.MixEnableCue((nint)destination, true, true);
        output.MixEnableCue((nint)replacement, true, true);
        check(calls == blocks + 1 && lastDestination == (nint)replacement && lastPcm.Any(v => v != 0),
            "Confirmation kept a stale vibration mixer after a graph change");
        output.CancelEnableCue();
        output.MixEnableCue((nint)replacement, true, true);
        check(calls == blocks + 1 && !output.HasEnableCue, "Cancelled confirmation continued playing");
    }
}
