// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Threading;

namespace DualSenseLighting.Core;

public enum HapticOutputMode { Automatic, DualSense, XInput, Bluetooth }

public static class HapticOutputSelection
{
    public static HapticOutputMode Resolve(HapticOutputMode requested, bool usb, bool bluetooth) => requested switch
    {
        HapticOutputMode.DualSense or HapticOutputMode.XInput or HapticOutputMode.Bluetooth => requested,
        _ => usb ? HapticOutputMode.DualSense : bluetooth ? HapticOutputMode.Bluetooth : HapticOutputMode.XInput,
    };

    public static bool SupportsFootstepAmplification(HapticOutputMode requested, bool usb, bool bluetooth) =>
        usb && Resolve(requested, usb, bluetooth) == HapticOutputMode.DualSense;
}

public readonly record struct RumbleMotors(ushort Low, ushort High)
{
    public static RumbleMotors FromLevel(float level)
    {
        var speed = (ushort)MathF.Round((float.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0) * ushort.MaxValue);
        return new(speed, speed);
    }
    public static RumbleMotors FromImpact(float level)
    {
        var low = FromLevel(level).Low;
        return new(low, (ushort)MathF.Round(low * 0.25f));
    }
    public RumbleMotors WithBalance(float balance)
    {
        balance = float.IsFinite(balance) ? Math.Clamp(balance, -1, 1) : 0;
        // Positive favors right; attenuate only, preserving silence and headroom.
        return new((ushort)MathF.Round(Low * (1 - Math.Max(0, balance))),
            (ushort)MathF.Round(High * (1 + Math.Min(0, balance))));
    }
    public static (float Left, float Right) MeasureStereo(float rms, float gain, HapticSpatialFrame spatial)
    {
        var level = Measure(rms, gain, default);
        return spatial.Valid ? (level * Math.Clamp(spatial.Left, 0, 1), level * Math.Clamp(spatial.Right, 0, 1)) : (level, level);
    }
    public static RumbleMotors Merge(RumbleMotors game, RumbleMotors plugin, RumbleMotors screen = default) =>
        new((ushort)Math.Min(ushort.MaxValue, Math.Max(game.Low, plugin.Low) + screen.Low),
            (ushort)Math.Min(ushort.MaxValue, Math.Max(game.High, plugin.High) + screen.High));

    public static float Measure(float rms, float gain, HapticSpatialFrame spatial)
    {
        if (!float.IsFinite(rms) || !float.IsFinite(gain) || rms <= 0 || gain <= 0) return 0;
        // Ordinary motors differ in frequency, not location. Keep distance fade,
        // without pretending that their balance represents left/right direction.
        var fade = spatial.Valid ? Math.Clamp(Math.Max(spatial.Left, spatial.Right), 0, 1) : 1;
        return Math.Clamp(rms * 8, 0, 1) * Math.Clamp(gain, 0, 1) * fade;
    }
}

public interface IRumbleDevices
{
    bool IsConnected(int slot);
    bool SetVibration(int slot, RumbleMotors motors);
    bool SetVibration(int slot, RumbleMotors motors, RumbleMotors screen) =>
        SetVibration(slot, RumbleMotors.Merge(default, motors, screen));
}

/// <summary>One worker selects one controller, smooths feedback, and releases its output.</summary>
public sealed class RumbleSession : IDisposable
{
    public const double StrengthTestPulseSeconds = 0.6;
    public const double StrengthTestGapSeconds = 0.35;
    public const double StrengthTestDuration = StrengthTestPulseSeconds * 2 + StrengthTestGapSeconds;
    private readonly IRumbleDevices devices;
    private readonly Func<bool> hasDualSense;
    private readonly RumblePulse pulse = new();
    private double nextScan;
    private double confirmationAt = double.NaN;
    private double strengthTestAt = double.NaN;
    private bool testChannels;
    private bool dualSense;
    private int slot = -1;
    private HapticOutputMode? mode;
    private int requestedSlot = -2;
    private HapticSettings? settings;
    private RumbleMotors sent;
    private RumbleMotors sentScreen;
    private float leftBalance = 1, rightBalance = 1, steeringPeak;
    private int usesRumble;

    public bool UsesRumble => Volatile.Read(ref usesRumble) != 0;
    public int Slot => Volatile.Read(ref slot);

    public RumbleSession(IRumbleDevices devices, Func<bool> hasDualSense)
    { this.devices = devices; this.hasDualSense = hasDualSense; }

    public bool Select(HapticOutputMode output, int preferredSlot, bool enabled, double seconds)
    {
        if (!Enum.IsDefined(output)) output = HapticOutputMode.Automatic;
        preferredSlot = Math.Clamp(preferredSlot, -1, 3);
        if (mode != output || requestedSlot != preferredSlot)
        { Release(); mode = output; requestedSlot = preferredSlot; nextScan = 0; }
        if (!enabled) { Release(); nextScan = 0; return true; }
        if (seconds < nextScan) return false;
        nextScan = seconds + 1;
        if (output == HapticOutputMode.Automatic) dualSense = hasDualSense();
        var regular = output == HapticOutputMode.XInput || output == HapticOutputMode.Automatic && !dualSense;
        Volatile.Write(ref usesRumble, regular ? 1 : 0);
        var selected = -1;
        if (regular)
        {
            if (preferredSlot >= 0) selected = devices.IsConnected(preferredSlot) ? preferredSlot : -1;
            else if (slot >= 0 && devices.IsConnected(slot)) selected = slot;
            else for (var i = 0; i < 4; i++) if (devices.IsConnected(i)) { selected = i; break; }
        }
        if (selected != slot) { StopMotors(); Volatile.Write(ref slot, selected); }
        return true;
    }

    public void RequestConfirmation(double seconds)
    { if (slot >= 0) { confirmationAt = seconds; strengthTestAt = double.NaN; } }

    public void RequestStrengthTest(double seconds, bool separateChannels = false)
    { if (slot >= 0) { strengthTestAt = seconds; testChannels = separateChannels; confirmationAt = double.NaN; pulse.Reset(); } }

    public void Tick(SfxLoudness signal, HapticSettings current, bool active, double seconds, float screenLevel = 0,
        (float Left, float Right)? stereo = null)
    {
        var peak = signal.TakePeak();
        // Bluetooth onset and steering must use the same consumed side sample.
        // The scalar handoff may arrive a tick before or after those sides.
        if (current.BluetoothRumble && stereo is { } measured) peak = Math.Max(measured.Left, measured.Right);
        if (!active || slot < 0 || !UsesRumble)
        { StopMotors(); settings = null; return; }
        if (!ReferenceEquals(settings, current))
        { pulse.Reset(); settings = current; leftBalance = rightBalance = 1; steeringPeak = 0; }
        var strength = float.IsFinite(current.RumbleStrength) ? Math.Clamp(current.RumbleStrength, 0, 1) : 0.5f;
        var level = pulse.Update(peak, seconds, current.RumblePulseMilliseconds) * strength;
        if (pulse.Started) { leftBalance = rightBalance = 1; steeringPeak = 0; }
        if (stereo is { } sides)
        {
            var maximum = Math.Max(sides.Left, sides.Right);
            if (float.IsFinite(maximum) && maximum > 0 && (pulse.Started || maximum >= steeringPeak))
            {
                leftBalance = Math.Clamp(sides.Left / maximum, 0, 1);
                rightBalance = Math.Clamp(sides.Right / maximum, 0, 1);
                steeringPeak = maximum;
            }
        }
        var motors = current.BluetoothRumble
            ? new RumbleMotors(RumbleMotors.FromLevel(level * leftBalance).Low, RumbleMotors.FromLevel(level * rightBalance).High)
            : RumbleMotors.FromImpact(level);
        if (!double.IsNaN(confirmationAt))
        {
            var elapsed = seconds - confirmationAt;
            var pulse = HapticEnableCue.PulseFrames / (double)HapticEnableCue.SampleRate;
            var gap = HapticEnableCue.GapFrames / (double)HapticEnableCue.SampleRate;
            if (elapsed < 0 || elapsed >= pulse * 2 + gap) confirmationAt = double.NaN;
            else if (elapsed < pulse || elapsed >= pulse + gap) motors = RumbleMotors.Merge(motors, RumbleMotors.FromLevel(0.25f));
        }
        // Screen movement follows its current camera sample independently of
        // the SFX release envelope and the enable confirmation.
        var screen = current.ScreenEffects ? RumbleMotors.FromLevel(screenLevel) : default;
        if (!double.IsNaN(strengthTestAt))
        {
            var elapsed = seconds - strengthTestAt;
            if (elapsed < 0 || elapsed >= StrengthTestDuration) strengthTestAt = double.NaN;
            else
            {
                var first = elapsed < StrengthTestPulseSeconds;
                var second = elapsed >= StrengthTestPulseSeconds + StrengthTestGapSeconds;
                var testLevel = first ? 0.2f : second ? 1f : 0;
                motors = testChannels
                    ? new RumbleMotors(first ? RumbleMotors.FromLevel(strength).Low : (ushort)0,
                        second ? RumbleMotors.FromLevel(strength).High : (ushort)0)
                    : current.BluetoothRumble ? RumbleMotors.FromLevel(testLevel * strength) : RumbleMotors.FromImpact(testLevel * strength);
                screen = default;
                pulse.Reset();
            }
        }
        if (current.BluetoothRumble)
        {
            // Calibrate the final limited mix; scaling layers before saturation
            // would lose the balance whenever combat and screen output overlap.
            motors = RumbleMotors.Merge(default, motors, screen).WithBalance(current.BluetoothMotorBalance);
            screen = default;
        }
        if (motors == sent && screen == sentScreen) return;
        if (!devices.SetVibration(slot, motors, screen))
        { StopMotors(); Volatile.Write(ref slot, -1); nextScan = seconds + 1; return; }
        sent = motors;
        sentScreen = screen;
    }

    private void StopMotors()
    {
        if (slot >= 0 && (sent != default || sentScreen != default)) devices.SetVibration(slot, default);
        sent = default;
        sentScreen = default;
        confirmationAt = double.NaN;
        strengthTestAt = double.NaN;
        pulse.Reset();
        leftBalance = rightBalance = 1; steeringPeak = 0;
    }

    public void Release()
    { StopMotors(); Volatile.Write(ref slot, -1); Volatile.Write(ref usesRumble, 0); settings = null; }
    public void Dispose() => Release();
}
