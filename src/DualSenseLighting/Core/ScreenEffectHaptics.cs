// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;

namespace DualSenseLighting.Core;

[Flags]
public enum ScreenEffectKind { None = 0, Shake = 1, RadialBlur = 2, DirectionalBlur = 4, GaussianBlur = 8, ChromaticAberration = 16 }

public sealed record ScreenEffectSample(ScreenEffectKind Effects, float Level, long Timestamp,
    ScreenEffectKind DominantEffect = ScreenEffectKind.None, long ExpiresAt = 0)
{
    internal ScreenEffectKind Texture => DominantEffect != ScreenEffectKind.None ? DominantEffect : Effects;
}

/// <summary>Maps the current authored camera effect to bounded controller samples.</summary>
internal sealed class ScreenEffectHaptics
{
    internal const int SampleRate = HapticEnableCue.SampleRate;
    internal const int Frames = HapticEnableCue.FramesPerBlock;
    internal const int Channels = HapticEnableCue.Channels;
    internal static readonly long FreshTicks = Stopwatch.Frequency / 10;
    private const int PatternFrames = SampleRate / 5;
    private static readonly float[][] patterns;
    private int position;
    private int pattern = -1;
    private float amplitude;
    public float OutputPeak { get; private set; }
    public float MinimumHeadroom { get; private set; } = 1;
    public int LimitedFrames { get; private set; }

    // Compute texture once during output setup, never on the audio render thread.
    static ScreenEffectHaptics() { patterns = CreatePatterns(); }

    public static ScreenEffectSample Measure(float shakeLevel,
        uint flags, int blurMode, float blurStrength, float blurWeight, float chromaticWeight,
        Vector3 chromaticParameters, long now)
    {
        var effects = ScreenEffectKind.None;
        var dominant = ScreenEffectKind.None;
        var level = 0f;
        void Include(ScreenEffectKind kind, float value)
        {
            if (!float.IsFinite(value) || value <= 0.0001f) return;
            effects |= kind;
            var bounded = Math.Clamp(value, 0, 1);
            if (bounded > level) { level = bounded; dominant = kind; }
        }
        Include(ScreenEffectKind.Shake, shakeLevel);
        if ((flags & (1u << 9)) != 0 && float.IsFinite(blurStrength) && float.IsFinite(blurWeight) && blurWeight is > 0 and <= 1)
            Include(blurMode switch { 0 => ScreenEffectKind.RadialBlur, 1 => ScreenEffectKind.GaussianBlur,
                2 => ScreenEffectKind.DirectionalBlur, _ => ScreenEffectKind.None },
                blurMode is >= 0 and <= 2 ? Math.Abs(blurStrength) * blurWeight : 0);
        if ((flags & (1u << 18)) != 0 && float.IsFinite(chromaticWeight) && chromaticWeight is > 0 and <= 1 && Valid(chromaticParameters))
            Include(ScreenEffectKind.ChromaticAberration,
                chromaticWeight * Math.Max(0, chromaticParameters.Z) * Math.Max(Math.Abs(chromaticParameters.X), Math.Abs(chromaticParameters.Y)));
        return new(effects, level, now, dominant);
    }

    private static bool Valid(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
        Math.Abs(value.X) <= 100 && Math.Abs(value.Y) <= 100 && Math.Abs(value.Z) <= 100;

    public static float Level(ScreenEffectSample? sample, long now)
    {
        if (sample == null || sample.Effects == ScreenEffectKind.None || !float.IsFinite(sample.Level) ||
            now < sample.Timestamp || now - sample.Timestamp > FreshTicks ||
            sample.ExpiresAt > 0 && now >= sample.ExpiresAt) return 0;
        return Math.Clamp(sample.Level, 0, 1);
    }

    public static float Response(float level, float strength) => float.IsFinite(level) && float.IsFinite(strength)
        ? MathF.Sqrt(Math.Clamp(level, 0, 1)) * Math.Clamp(strength, 0, 1) : 0;

    // Only the audio thread advances this oscillator. No allocations or queued effects.
    public bool Render(Span<float> output, float level, ScreenEffectKind effect = ScreenEffectKind.Shake,
        ReadOnlySpan<float> nativeInput = default)
    {
        if (output.Length != Frames * Channels) throw new ArgumentException("Expected one native bed block.", nameof(output));
        if (!nativeInput.IsEmpty && nativeInput.Length != output.Length)
            throw new ArgumentException("Expected one native bed block.", nameof(nativeInput));
        output.Clear();
        OutputPeak = 0; MinimumHeadroom = 1; LimitedFrames = 0;
        var nextPattern = (effect & ScreenEffectKind.Shake) != 0 ? 0 :
            (effect & ScreenEffectKind.RadialBlur) != 0 ? 1 :
            (effect & ScreenEffectKind.DirectionalBlur) != 0 ? 2 :
            (effect & ScreenEffectKind.GaussianBlur) != 0 ? 3 :
            (effect & ScreenEffectKind.ChromaticAberration) != 0 ? 4 : -1;
        var target = nextPattern >= 0 && float.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        if (target <= 0) { Reset(); return false; }
        if (nextPattern != pattern) { pattern = nextPattern; amplitude = 0; }
        amplitude = Math.Min(amplitude, target); // Current weaker frames never inherit a stronger tail.
        var texture = patterns[pattern];
        for (var frame = 0; frame < Frames; frame++)
        {
            // A short attack prevents clicks; zero and weaker levels apply immediately.
            amplitude += Math.Clamp(target - amplitude, -1f / 240, 1f / 240);
            var value = amplitude * texture[position];
            if (!nativeInput.IsEmpty)
            {
                var peak = Math.Max(Math.Abs(nativeInput[frame * Channels]), Math.Abs(nativeInput[frame * Channels + 1]));
                var headroom = float.IsFinite(peak) ? Math.Max(0, 1 - peak) : 0;
                MinimumHeadroom = Math.Min(MinimumHeadroom, headroom);
                if (Math.Abs(value) > headroom) LimitedFrames++;
                value = Math.Clamp(value, -headroom, headroom);
            }
            output[frame * Channels] = output[frame * Channels + 1] = value;
            OutputPeak = Math.Max(OutputPeak, Math.Abs(value));
            position = (position + 1) % PatternFrames;
        }
        return OutputPeak > 0;
    }

    public void Reset()
    { amplitude = 0; position = 0; pattern = -1; OutputPeak = 0; MinimumHeadroom = 1; LimitedFrames = 0; }

    private static float[][] CreatePatterns()
    {
        var result = new float[5][];
        for (var index = 0; index < result.Length; index++) result[index] = new float[PatternFrames];
        for (var frame = 0; frame < PatternFrames; frame++)
        {
            var time = frame / (float)SampleRate;
            float Tone(float frequency) => MathF.Sin(2 * MathF.PI * frequency * time);
            // Shake rhythm comes entirely from observed motion. This single
            // carrier adds no independent beats or random rumble to its envelope.
            result[0][frame] = Tone(60);
            result[1][frame] = 0.85f * Tone(65) + 0.15f * Tone(130);
            result[2][frame] = 0.80f * Tone(95) + 0.20f * Tone(190);
            result[3][frame] = Tone(100);
            result[4][frame] = 0.85f * Tone(180) + 0.15f * Tone(240);
        }
        return result;
    }
}
