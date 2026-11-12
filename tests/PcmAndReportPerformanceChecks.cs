// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using DualSenseLighting.Core;

internal static class PcmAndReportPerformanceChecks
{
    internal delegate float Rms(ReadOnlySpan<float> samples);
    internal static void Run(Action<bool, string> check) => CheckRms(check, SfxLoudness.MeasureRms);

    // Also used against the actual compiled plugin in both build flavors.
    internal static void CheckRms(Action<bool, string> check, Rms measure)
    {
        var random = new Random(20261112);
        foreach (var length in new[] { 0, 1, 7, 8, 15, 16, 17, 31, 32, 33, 2048, 4096 })
        {
            var samples = new float[length + 1];
            for (var run = 0; run < 100; run++)
            {
                var gain = run % 3 == 0 ? 16f : run % 3 == 1 ? .8f : 1e-20f;
                for (var i = 1; i < samples.Length; i++) samples[i] = (float)(random.NextDouble() * 2 - 1) * gain;
                var block = samples.AsSpan(1);
                var expected = Scalar(block); var actual = measure(block);
                check(float.IsFinite(actual) && Math.Abs(BitConverter.SingleToInt32Bits(expected) -
                    BitConverter.SingleToInt32Bits(actual)) <= 1, "Vector RMS differs from double scalar reference");
            }
        }
        foreach (var length in new[] { 17, 2048, 4096 })
        {
            var samples = new float[length]; Array.Fill(samples, .25f);
            foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 16.000002f, -16.000002f })
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = invalid;
                check(measure(samples) == 0, "An invalid PCM sample escaped whole-block validation at " + index);
                samples[index] = .25f;
            }
            Array.Fill(samples, 16); check(measure(samples) == 1, "Exact upper PCM boundary rejected");
            Array.Fill(samples, -16); check(measure(samples) == 1, "Exact lower PCM boundary rejected");
            Array.Clear(samples); check(measure(samples) == 0, "Silent PCM changed");
            Array.Fill(samples, .125f);
            var copy = (float[])samples.Clone();
            Action work = () => _ = measure(samples);
            var bytes = AllocationChecks.MeasureAllocations(work, warmup: 2000, iterations: 1000);
            check(bytes == 0 && samples.AsSpan().SequenceEqual(copy), "RMS allocated or modified source PCM");
        }
    }

    private static float Scalar(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0;
        double energy = 0;
        foreach (var sample in samples)
        {
            if (!float.IsFinite(sample) || Math.Abs(sample) > 16) return 0;
            energy += (double)sample * sample;
        }
        return (float)Math.Min(1, Math.Sqrt(energy / samples.Length));
    }

    internal static void CheckReports(Action<bool, string> check)
    {
        var random = new Random(20261112);
        foreach (var length in new[] { 48, 63, 64, 78 })
        foreach (var lighting in new[] { false, true })
        foreach (var rumble in length == 78 ? new[] { false, true } : new[] { false })
        foreach (var improved in new[] { false, true })
        for (var sequence = 0; sequence < 256; sequence++)
        {
            LightingFrame? frame = lighting ? new((byte)random.Next(256), (byte)random.Next(256),
                (byte)random.Next(256), (byte)random.Next(256), sequence % 2 == 0) : null;
            RumbleMotors? motors = rumble ? new((ushort)random.Next(65536), (ushort)random.Next(65536)) : null;
            var reference = new byte[length];
            reference[0] = length == 78 ? (byte)0x31 : (byte)2;
            if (length == 78)
            { reference[1] = (byte)((sequence & 15) << 4); reference[2] = 0x10; DualSenseReport.SetChecksum(reference); }
            if (frame.HasValue) DualSenseReport.Apply(reference, true, true, frame.Value);
            if (motors.HasValue)
            {
                reference[3] = improved ? (byte)2 : (byte)3;
                if (improved) reference[41] |= 4;
                reference[5] = (byte)(motors.Value.High >> 8); reference[6] = (byte)(motors.Value.Low >> 8);
                DualSenseReport.SetChecksum(reference);
            }
            var actual = DualSenseReport.Create(length, frame, motors, (byte)sequence, improved);
            check(reference.AsSpan().SequenceEqual(actual), "Single-final-checksum report changed output bytes");
            if (length == 78)
            {
                check(DualSenseReport.HasValidChecksum(actual), "Created Bluetooth report has an invalid CRC");
                actual[10] ^= 1; var corrupt = (byte[])actual.Clone();
                check(DualSenseReport.Apply(actual, true, true, frame ?? default) == ReportAction.Unchanged &&
                    actual.AsSpan().SequenceEqual(corrupt), "Incoming Bluetooth CRC validation was bypassed");
            }
        }
    }
}
