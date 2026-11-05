// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DualSenseLighting.Core;

/// <summary>Real worker/report paths with a checksum sink; no retained report arrays.</summary>
internal static class LoopPerformanceChecks
{
    internal sealed record Sample(double MicrosecondsPerTick, long AllocatedBytes, long Writes, ulong Checksum);
    internal sealed record Result(string Case, int Devices, int ReportLength, int Iterations, string AllocationScope, Sample[] Samples)
    {
        public double P50BatchMicrosecondsPerTick => Percentile(0.50);
        public double P95BatchMicrosecondsPerTick => Percentile(0.95);
        private double Percentile(double percentile)
        {
            var values = Array.ConvertAll(Samples, sample => sample.MicrosecondsPerTick);
            Array.Sort(values);
            return values[Math.Max(0, (int)Math.Ceiling(values.Length * percentile) - 1)];
        }
    }

    internal static async Task<List<Result>> Run()
    {
        var results = new List<Result>();
        results.Add(await Measure(63, 0, "idle process control", 1000, false));
        foreach (var length in new[] { 63, 64, 78 })
        foreach (var count in new[] { 0, 1, 2, 8 })
        foreach (var animated in new[] { false, true })
            results.Add(await Measure(length, count, animated ? "animation" : "unchanged+refresh", 1000, animated));
        results.Add(await Measure(78, 2, "forced-async writes", 100, true, asynchronous: true));
        foreach (var scenario in new[] { "discovery failure", "null open", "write failure", "reconnect", "background release", "disable release", "caller cancellation", "deadline" })
            results.Add(await Measure(78, 2, scenario, 1, false));
        return results;
    }

    private static async Task<Result> Measure(int length, int count, string scenario, int iterations, bool animated, bool asynchronous = false)
    {
        var samples = new Sample[10];
        for (var batch = 0; batch < samples.Length; batch++)
        {
            var devices = new Devices(length, count, asynchronous);
            var foreground = true;
            var errors = 0;
            using var session = new LightingSession(devices, () => foreground, _ => errors++);
            var settings = LightingSettings.Default with { Effect = animated ? LightingEffect.Rainbow : LightingEffect.Solid };
            await session.TickAsync(settings, 0, default);
            if (length == 78) session.SetVibration(0, new RumbleMotors(12000, 18000));
            // Same warmup policy per sample; reset scenario state outside measured work.
            for (var i = 1; i <= 200; i++) await session.TickAsync(settings, i / 30.0, default);
            var seconds = 10.0;
            using var cancelled = new CancellationTokenSource();
            if (scenario == "discovery failure") devices.FailScan = true;
            if (scenario == "null open") { session.Dispose(); devices.NullOpen = true; }
            if (scenario == "write failure") devices.FailWrite = true;
            if (scenario == "deadline") devices.Stall = true;
            if (scenario == "background release") foreground = false;
            if (scenario == "disable release") settings = settings with { Enabled = false };
            if (scenario == "caller cancellation") cancelled.Cancel();
            if (scenario == "reconnect") session.Dispose();
            var writes = devices.Writes;
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++)
            {
                try
                {
                    if (scenario == "idle process control") await Task.CompletedTask;
                    else await session.TickAsync(settings, seconds + i / 30.0, cancelled.Token);
                }
                catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
            }
            var elapsed = Stopwatch.GetTimestamp() - start;
            var delta = GC.GetTotalAllocatedBytes(precise: true) - allocated;
            var accepted = devices.Writes - writes;
            if (scenario == "caller cancellation" && accepted != 0) throw new Exception("Cancelled benchmark wrote a report");
            if ((scenario == "deadline" || scenario == "write failure" || scenario == "discovery failure") && errors == 0)
                throw new Exception("Failure benchmark did not reach its failure path: " + scenario);
            if (count != 0 && (scenario == "animation" || scenario == "forced-async writes") && accepted != (long)iterations * count)
                throw new Exception($"Animation benchmark writes={accepted}, expected={(long)iterations * count}; scenario={scenario}, length={length}, devices={count}, batch={batch}");
            samples[batch] = new(elapsed * 1_000_000.0 / Stopwatch.Frequency / iterations, delta, accepted, devices.Checksum);
            devices.FailWrite = devices.Stall = false;
            await session.ReleaseAsync(true, default);
        }
        return new(scenario, count, length, iterations, "Process total across await; includes task/runtime background noise; native/driver allocations excluded", samples);
    }

    private sealed class Devices(int length, int count, bool asynchronous) : ILightingDevices
    {
        private readonly string[] paths = Paths(count);
        internal bool FailScan, NullOpen, FailWrite, Stall;
        internal long Writes;
        internal ulong Checksum;
        public IReadOnlyCollection<string> GetPaths() => FailScan ? throw new InvalidOperationException("Benchmark discovery failure") : paths;
        public ILightingDevice? Open(string path) => NullOpen ? null : new Device(this, length, asynchronous);
        private static string[] Paths(int count)
        {
            var result = new string[count];
            for (var i = 0; i < count; i++) result[i] = "synthetic-device-" + i;
            return result;
        }
        private sealed class Device(Devices owner, int length, bool asynchronous) : ILightingDevice
        {
            private bool disposed;
            public int ReportLength => length;
            public async Task WriteAsync(byte[] report, CancellationToken cancellationToken)
            {
                if (disposed) throw new ObjectDisposedException(nameof(Device));
                if (owner.Stall) await Task.Delay(Timeout.Infinite, cancellationToken);
                if (owner.FailWrite) throw new InvalidOperationException("Benchmark write failure");
                if (asynchronous) await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                if (report.Length != length) throw new Exception("Unexpected report length");
                foreach (var value in report) owner.Checksum = unchecked(owner.Checksum * 31 + value);
                owner.Writes++;
            }
            public void Dispose() => disposed = true;
        }
    }
}
