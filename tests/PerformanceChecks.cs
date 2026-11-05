// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DualSenseLighting.Core;
using DualSenseLighting.Ui;

internal static class PerformanceChecks
{
    private static double sink;
    private sealed record Result(string Case, int Iterations, double MicrosecondsPerCall, long AllocatedBytes);

    internal static async Task Run()
    {
        var samples = new float[4096];
        Array.Fill(samples, 0.1f);
        var output = new float[2048];
        var matrix = new float[32];
        matrix[0] = matrix[16] = 0.5f;
        matrix[1] = 0.5f; matrix[17] = -0.5f;
        var spatial = new HapticSpatialFrame(true, 0.7f, 5, 0.3f, 1);
        var listener = new HapticListenerPose(Vector3.Zero, Vector3.UnitX, 1, new(0, 2, -10));
        var haptics = new HapticSettings(Enabled: true, Spatial: true).Normalize();
        var lighting = LightingSettings.Default with { Effect = LightingEffect.SfxColorBlend };
        var envelope = new SfxEnvelope();
        var text = new UiText { Language = UiLanguage.Chinese };
        var results = new List<Result>();
        results.Add(Measure("RMS, 4096 samples", 20000, _ => sink += SfxLoudness.MeasureRms(samples)));
        results.Add(Measure("Spatial PCM, 16 channels", 20000, _ =>
        {
            if (!HapticSpatializer.Render(samples, matrix, output, spatial)) throw new Exception("Invalid benchmark PCM");
            sink += output[0];
        }));
        results.Add(Measure("Spatial position and impact bias", 100000, _ =>
        {
            var frame = HapticSpatializer.Evaluate(haptics, listener, new(0.1f, 0, 1), 1);
            sink += HapticSpatializer.BiasTowardImpact(haptics, listener, new(0.1f, 0, 0), frame, new(2, 0, 1)).Left;
        }));
        results.Add(Measure("Lighting render and sound envelope", 100000, i =>
        {
            var level = envelope.Update(0.1f, i / 30.0, lighting, true);
            sink += LightingEffects.Render(lighting, i / 30.0, level).Red;
        }));
        results.Add(Measure("Cached localized UI labels", 100000, _ => sink += text.Label("Lighting").Length));
        var loops = await LoopPerformanceChecks.Run();
#if BASELINE_PERFORMANCE
        object? roster = null; // The frozen baseline predates the production SelectRoster seam.
#else
        var roster = RosterPerformanceChecks.Run();
#endif
        Console.WriteLine(JsonSerializer.Serialize(new { Kind = "Synthetic CPU/worker replay; not in-game FPS or physical HID latency",
            Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), ProcessorCount = Environment.ProcessorCount,
            CpuIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unavailable",
            AssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(typeof(PerformanceChecks).Assembly.Location))),
            HelperWarmup = 2000, LoopWarmup = 200, LoopSamples = 10,
            Cases = results, DeviceLoops = loops, RosterLoops = roster, RosterScope = "Snapshot-copy + real exclusion/state/selection/publication; excludes Dalamud reads, cast/arena adapters and full framework Update", Checksum = sink }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var result in results)
            if (result.AllocatedBytes != 0) throw new Exception(result.Case + " allocated on a warmed hot path: " + result.AllocatedBytes);
    }

    private static Result Measure(string name, int iterations, Action<int> action)
    {
        for (var i = 0; i < 2000; i++) action(i);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) action(i);
        var elapsed = Stopwatch.GetTimestamp() - start;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return new(name, iterations, elapsed * 1_000_000.0 / Stopwatch.Frequency / iterations, allocated);
    }
}
