// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Numerics;
using System.Threading;

namespace DualSenseLighting.Core;

/// <summary>Bounded, allocation-free audio-to-worker handoff. Keeps the loudest voice RMS per tick.</summary>
public sealed class SfxLoudness
{
    // High word: generation (odd means active). Low word: positive float bits.
    // Keeping both in one CAS prevents a callback from republishing audio after a reset.
    private long state;
    public uint Generation => (uint)((ulong)Volatile.Read(ref state) >> 32);
    public bool Active => (Generation & 1) != 0;

    public void SetActive(bool active)
    {
        while (true)
        {
            var before = Volatile.Read(ref state);
            var generation = (uint)((ulong)before >> 32);
            if (((generation & 1) != 0) == active) return;
            var after = (long)((ulong)unchecked(generation + 1) << 32);
            if (Interlocked.CompareExchange(ref state, after, before) == before) return;
        }
    }

    public void Observe(ReadOnlySpan<float> samples) => Observe(samples, Generation);

    public void Observe(ReadOnlySpan<float> samples, uint generation)
    {
        var before = Volatile.Read(ref state);
        if ((generation & 1) == 0 || (uint)((ulong)before >> 32) != generation || samples.IsEmpty) return;
        ObserveLevel(MeasureRms(samples), generation);
    }

    // Accept only a level produced by whole-block PCM validation. Generation
    // and level remain one atomic value, exactly as in the span entry point.
    public void ObserveLevel(float rms, uint generation)
    {
        var before = Volatile.Read(ref state);
        if ((generation & 1) == 0 || (uint)((ulong)before >> 32) != generation ||
            !float.IsFinite(rms) || rms <= 0 || rms > 1) return;
        var bits = BitConverter.SingleToUInt32Bits(rms);
        for (var attempt = 0; attempt < 4 && (uint)((ulong)before >> 32) == generation; attempt++)
        {
            if ((uint)before >= bits) return;
            var after = (long)(((ulong)generation << 32) | bits);
            var current = Interlocked.CompareExchange(ref state, after, before);
            if (current == before) return;
            before = current;
        }
    }

    public static float MeasureRms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0;
        double energy = 0;
        var index = 0;
        if (Vector.IsHardwareAccelerated && samples.Length >= Vector<float>.Count * 2)
        {
            var lower = Vector<double>.Zero;
            var upper = Vector<double>.Zero;
            var limit = new Vector<float>(16);
            for (; index <= samples.Length - Vector<float>.Count; index += Vector<float>.Count)
            {
                var values = new Vector<float>(samples.Slice(index, Vector<float>.Count));
                // NaN and infinities also fail this comparison. Widen before squaring
                // to retain the scalar path's double precision and tiny signals.
                if (!Vector.LessThanOrEqualAll(Vector.Abs(values), limit)) return 0;
                Vector.Widen(values, out Vector<double> left, out Vector<double> right);
                lower += left * left;
                upper += right * right;
            }
            energy = Vector.Sum(lower) + Vector.Sum(upper);
        }
        for (; index < samples.Length; index++)
        {
            var sample = samples[index];
            // Reject an invalid block as a whole instead of turning a bad sample into a flash.
            if (!float.IsFinite(sample) || Math.Abs(sample) > 16) return 0;
            energy += (double)sample * sample;
        }
        return (float)Math.Min(1, Math.Sqrt(energy / samples.Length));
    }

    public float TakePeak()
    {
        while (true)
        {
            var before = Volatile.Read(ref state);
            var after = (long)((ulong)before & 0xffffffff00000000UL);
            if (Interlocked.CompareExchange(ref state, after, before) == before)
                return BitConverter.UInt32BitsToSingle((uint)before);
        }
    }
}

/// <summary>One worker owns the envelope; all lighting consumers share its published level.</summary>
public sealed class SfxEnvelope
{
    private double lastTime = double.NaN;
    private float level;

    public float Update(float rms, double seconds, LightingSettings settings, bool active)
    {
        if (!active || !double.IsFinite(seconds) || seconds < 0 || seconds < lastTime)
        { lastTime = double.NaN; return level = 0; }
        var s = settings.Normalize();
        // Keep the gate at 1.6% of the light's response range as sensitivity changes.
        // At the default 8x this equals the user-validated raw threshold of 0.002.
        var gate = s.SfxAutomaticGate ? 0.016f / s.SfxSensitivity : s.SfxNoiseGate;
        var target = float.IsFinite(rms)
            ? Math.Clamp((rms - gate) * s.SfxSensitivity, 0, 1) : 0;
        var elapsed = double.IsNaN(lastTime) ? 1.0 / 30 : seconds - lastTime;
        lastTime = seconds;
        level = target >= level ? target : (float)(target + (level - target) * Math.Exp(-elapsed / s.SfxReleaseSeconds));
        if (level < 0.001f) level = 0;
        return level;
    }
}
