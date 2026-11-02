// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Numerics;

namespace DualSenseLighting.Core;

public enum PlayerIndicatorAnimation { Steady, Chase, Bounce, Blink, Heartbeat, Sparkle, Loading }

/// <summary>Independent white-LED animations; all results are five-bit on/off masks.</summary>
public static class PlayerIndicatorEffects
{
    private static readonly byte[] SteadyMasks = [0, 4, 10, 21, 27, 31];
    private static readonly byte[][] SparkleOrders = CreateSparkleOrders();

    public static int Count(byte mask) => BitOperations.PopCount((uint)(mask & 0x1f));
    public static byte MaskForCount(int count) => SteadyMasks[Math.Clamp(count, 0, 5)];

    public static byte Render(byte selectedMask, PlayerIndicatorAnimation animation, float cycleSeconds, double seconds)
    {
        selectedMask &= 0x1f;
        var count = Count(selectedMask);
        if (count == 0 || animation == PlayerIndicatorAnimation.Steady) return selectedMask;
        var cycle = float.IsFinite(cycleSeconds) ? Math.Clamp(cycleSeconds, 1, 12) : 2;
        var time = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        // Remove modulo rounding noise so a discrete step repeats at the same cycle boundary.
        var phase = Math.Round(time % cycle / cycle, 9);
        if (phase >= 1) phase = 0;
        switch (animation)
        {
            case PlayerIndicatorAnimation.Chase:
                return SelectedBit(selectedMask, Step(phase, count));
            case PlayerIndicatorAnimation.Bounce:
                var travel = 5 - count;
                if (travel == 0) return 31;
                var step = Step(phase, travel * 2);
                var position = step <= travel ? step : travel * 2 - step;
                return (byte)(((1 << count) - 1) << position);
            case PlayerIndicatorAnimation.Blink:
                return phase < 0.5 ? selectedMask : (byte)0;
            case PlayerIndicatorAnimation.Heartbeat:
                return phase < 0.12 || (phase >= 0.22 && phase < 0.38) ? selectedMask : (byte)0;
            case PlayerIndicatorAnimation.Sparkle:
                var beat = phase * SparkleOrders.Length;
                if (beat % 1 >= 0.65) return 0;
                var order = SparkleOrders[Step(phase, SparkleOrders.Length)];
                byte sparkle = 0;
                for (var i = 0; i < count; i++) sparkle |= order[i];
                return sparkle;
            case PlayerIndicatorAnimation.Loading:
                var filled = Step(phase, count + 1);
                byte loading = 0;
                for (var i = 0; i < filled; i++) loading |= SelectedBit(selectedMask, i);
                return loading;
            default:
                return selectedMask;
        }
    }

    // Account for the phase's decimal rounding when a slot starts at a fraction such as 1/6.
    private static int Step(double phase, int steps) => Math.Min((int)(phase * steps + 1e-8), steps - 1);

    private static byte SelectedBit(byte mask, int index)
    {
        for (var bit = 0; bit < 5; bit++)
        {
            if ((mask & (1 << bit)) == 0) continue;
            if (index-- == 0) return (byte)(1 << bit);
        }
        return 0;
    }

    private static byte[][] CreateSparkleOrders()
    {
        // Cache seeded shuffles once so native output and preview never make different random choices.
        var random = new Random(0x5d5);
        var orders = new byte[8][];
        for (var i = 0; i < orders.Length; i++)
        {
            byte[] bits = [1, 2, 4, 8, 16];
            random.Shuffle(bits.AsSpan());
            orders[i] = bits;
        }
        return orders;
    }
}
