// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

/// <summary>A finite confirmation pattern, advanced only by the native audio thread.</summary>
internal sealed class HapticEnableCue
{
    internal const int SampleRate = 48000;
    internal const int FramesPerBlock = 256;
    internal const int Channels = 8;
    internal const int PulseFrames = SampleRate / 10;
    internal const int GapFrames = SampleRate / 10;
    internal const int TotalFrames = PulseFrames * 2 + GapFrames;
    private static readonly float[] pulse;
    private readonly long expiresAt;
    private int position;

    // Explicit initialization runs on the requesting worker, never on first audio render.
    static HapticEnableCue() { pulse = CreatePulse(); }
    internal HapticEnableCue(long expiresAt) { this.expiresAt = expiresAt; }
    internal bool Finished(long now) => position >= TotalFrames || now >= expiresAt;

    internal bool Render(Span<float> output, long now)
    {
        if (output.Length != FramesPerBlock * Channels) throw new ArgumentException("Expected one native bed block.", nameof(output));
        output.Clear();
        if (Finished(now)) return false;
        for (var frame = 0; frame < FramesPerBlock && position < TotalFrames; frame++, position++)
        {
            var sample = position < PulseFrames ? pulse[position]
                : position >= PulseFrames + GapFrames ? pulse[position - PulseFrames - GapFrames] : 0;
            // Both grips, through the same bed format as existing spatial haptics.
            output[frame * Channels] = output[frame * Channels + 1] = sample;
        }
        return true;
    }

    private static float[] CreatePulse()
    {
        var samples = new float[PulseFrames];
        const int ramp = SampleRate / 200; // 5 ms attack/release avoids an abrupt click.
        for (var i = 0; i < samples.Length; i++)
        {
            var envelope = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / (float)ramp);
            samples[i] = 0.25f * envelope * MathF.Sin(2 * MathF.PI * 160 * i / SampleRate);
        }
        return samples;
    }
}
