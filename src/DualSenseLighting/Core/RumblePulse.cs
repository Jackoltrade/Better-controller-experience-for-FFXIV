// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

/// <summary>One bounded impact per sound onset, with no queue or sustained-sound retrigger.</summary>
public sealed class RumblePulse
{
    public const int DefaultMilliseconds = 120;
    public const int MinimumMilliseconds = 60;
    public const int MaximumMilliseconds = 300;
    private double lastTime = double.NaN, started = double.NegativeInfinity, lastSound = double.NegativeInfinity;
    private float previous, peak;
    private bool armed = true;
    public bool Started { get; private set; }

    public void Reset()
    {
        lastTime = double.NaN; started = lastSound = double.NegativeInfinity;
        previous = peak = 0; armed = true;
        Started = false;
    }

    public float Update(float input, double seconds, int milliseconds)
    {
        Started = false;
        if (!double.IsFinite(seconds) || seconds < 0 || seconds < lastTime)
        { Reset(); return 0; }
        input = float.IsFinite(input) ? Math.Clamp(input, 0, 1) : 0;
        var duration = Math.Clamp(milliseconds, MinimumMilliseconds, MaximumMilliseconds) / 1000.0;
        var onset = input > 0.005f && (armed || input - previous >= Math.Max(0.015f, previous * 0.5f));
        if (input <= 0.005f)
        {
            if (seconds - lastSound >= 0.07) armed = true;
        }
        else
        {
            lastSound = seconds;
            if (onset && seconds - started >= duration + 0.1)
            { started = seconds; peak = input; Started = true; }
            // Merge the first audio layers into this impact; do not extend it.
            else if (seconds - started <= 0.04) peak = Math.Max(peak, input);
            armed = false;
        }
        previous = input;
        lastTime = seconds;
        var elapsed = seconds - started;
        if (elapsed < 0 || elapsed >= duration) return 0;
        var fade = Math.Clamp((elapsed / duration - 0.6) / 0.4, 0, 1);
        return peak * (float)(1 - fade * fade * (3 - 2 * fade));
    }
}
