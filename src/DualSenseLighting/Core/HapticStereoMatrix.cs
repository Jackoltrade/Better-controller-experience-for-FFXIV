// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

public static class HapticStereoMatrix
{
    public static float[] FromNative(ReadOnlySpan<float> matrix, int inputChannels)
    {
        if (inputChannels is not (8 or 16) || matrix.Length < inputChannels * inputChannels)
            throw new NotSupportedException("Native stereo matrix dimensions are unsupported.");
        // Native rows are INPUT channels, padded to 8/16 output lanes. Convert
        // their first two lanes to the compact output-by-input layout we use.
        var result = new float[inputChannels * 2];
        var energy = 0f;
        for (var input = 0; input < inputChannels; input++)
        for (var output = 0; output < 2; output++)
        {
            var coefficient = matrix[input * inputChannels + output];
            if (!float.IsFinite(coefficient) || MathF.Abs(coefficient) > 4)
                throw new NotSupportedException("Native stereo conversion matrix is unsupported.");
            result[output * inputChannels + input] = coefficient;
            energy += coefficient * coefficient;
        }
        if (energy < 0.1f) throw new NotSupportedException("Native stereo conversion matrix is empty.");
        // The native 8 -> 2 downmix intentionally has 0.89 direct / 0.11 cross
        // feed. Validate handedness, rather than demanding zero crossfeed.
        if (inputChannels == 8 && (result[0] <= 0.1f || result[9] <= 0.1f ||
            result[0] <= MathF.Abs(result[8]) || result[9] <= MathF.Abs(result[1])))
            throw new NotSupportedException("Native left/right channel mapping is unsupported.");
        return result;
    }
}
