// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DualSenseLighting.Core;

public interface ILightingDevice : IDisposable
{
    int ReportLength { get; }
    bool ImprovedRumble => false;
    Task WriteAsync(byte[] report, CancellationToken cancellationToken);
}

public interface ILightingDevices
{
    IReadOnlyCollection<string> GetPaths();
    ILightingDevice? Open(string path);
}

/// <summary>One worker owns lighting and optional Bluetooth rumble. No inputs are read.</summary>
public sealed class LightingSession : IRumbleDevices, IDisposable
{
    private readonly ILightingDevices devices;
    private readonly Func<bool> isForeground;
    private readonly Action<Exception> onError;
    private readonly Dictionary<string, ILightingDevice> open = new(StringComparer.OrdinalIgnoreCase);
    private sealed class OutputState
    {
        public LightingFrame? Lighting;
        public RumbleMotors Motors;
        public double Seconds;
        public byte Sequence;
    }
    private readonly Dictionary<string, OutputState> sent = new(StringComparer.OrdinalIgnoreCase);
    private string? bluetoothPath;
    private RumbleMotors bluetoothMotors;
    private bool bluetoothRequested;
    private int usbCount, bluetoothCount;
    private long bluetoothGeneration;
    private int bluetoothCommand, bluetoothImproved;
    private int testMotorRange, testWrites;
    private int testFirstCommand, testSecondCommand;
    private double testStarted = double.NegativeInfinity;
    private double testUntil = double.NegativeInfinity;
    private double nextScan;
    private int deviceCount;
    private long writeCount;
    private long releaseCount;

    public int DeviceCount => Volatile.Read(ref deviceCount);
    public long WriteCount => Interlocked.Read(ref writeCount);
    public long ReleaseCount => Interlocked.Read(ref releaseCount);
    public int UsbCount => Volatile.Read(ref usbCount);
    public int BluetoothCount => Volatile.Read(ref bluetoothCount);
    public bool HasUsb => UsbCount != 0;
    public bool HasBluetooth => BluetoothCount != 0;
    public long BluetoothGeneration => Interlocked.Read(ref bluetoothGeneration);
    public bool BluetoothImprovedRumble => Volatile.Read(ref bluetoothImproved) != 0;
    public (byte Left, byte Right) BluetoothMotorCommand
    {
        get { var value = Volatile.Read(ref bluetoothCommand); return ((byte)value, (byte)(value >> 8)); }
    }
    public (byte Minimum, byte Maximum, int Writes) BluetoothStrengthTest
    {
        get { var range = Volatile.Read(ref testMotorRange); return ((byte)range, (byte)(range >> 8), Volatile.Read(ref testWrites)); }
    }
    public (byte FirstLeft, byte FirstRight, byte SecondLeft, byte SecondRight) BluetoothTestCommands
    {
        get
        {
            var first = Volatile.Read(ref testFirstCommand); var second = Volatile.Read(ref testSecondCommand);
            return ((byte)first, (byte)(first >> 8), (byte)second, (byte)(second >> 8));
        }
    }
    public void StartStrengthTest(double seconds)
    {
        Volatile.Write(ref testMotorRange, 0); Volatile.Write(ref testWrites, 0);
        Volatile.Write(ref testFirstCommand, 0); Volatile.Write(ref testSecondCommand, 0);
        testStarted = seconds;
        testUntil = seconds + RumbleSession.StrengthTestDuration;
    }

    public LightingSession(ILightingDevices devices, Func<bool> isForeground, Action<Exception> onError)
    {
        this.devices = devices;
        this.isForeground = isForeground;
        this.onError = onError;
    }

    public async Task TickAsync(LightingSettings settings, double seconds, CancellationToken cancellationToken, float sfxLevel = 0,
        LimitBreakState? limitBreak = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((!settings.Enabled && !bluetoothRequested) || !isForeground())
        {
            await ReleaseAsync(!settings.Enabled, cancellationToken).ConfigureAwait(false);
            return;
        }

        Discover(settings.Enabled, bluetoothRequested, seconds);

        var frame = LightingEffects.Render(settings, seconds, sfxLevel, limitBreak);
        foreach (var path in open.Keys.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Check again after discovery and before every write, including multi-controller setups.
            if (!isForeground()) { await ReleaseAsync(false, cancellationToken).ConfigureAwait(false); return; }
            var bluetooth = open[path].ReportLength == DualSenseReport.BluetoothLength;
            var desiredLighting = settings.Enabled ? frame : (LightingFrame?)null;
            var motors = bluetooth && path == bluetoothPath ? bluetoothMotors : default;
            if (!sent.TryGetValue(path, out var previous)) previous = new OutputState();
            // Static light/rumble refresh also recovers another app's one-shot update.
            if (previous.Lighting == desiredLighting && previous.Motors == motors &&
                (desiredLighting == null && motors == default || seconds - previous.Seconds < 1)) continue;
            var lights = desiredLighting ?? (previous.Lighting.HasValue ? new LightingFrame(0, 0, 0, 0) : (LightingFrame?)null);
            var vibration = bluetooth && (motors != default || previous.Motors != default) ? motors : (RumbleMotors?)null;
            if (lights == null && vibration == null) continue;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(500);
                var report = DualSenseReport.Create(open[path].ReportLength, lights, vibration, previous.Sequence++, open[path].ImprovedRumble);
                await open[path].WriteAsync(report, deadline.Token).ConfigureAwait(false);
                if (path == bluetoothPath && vibration.HasValue)
                {
                    Volatile.Write(ref bluetoothCommand, (motors.Low >> 8) | (motors.High >> 8 << 8));
                    if (motors != default && seconds >= testStarted && seconds <= testUntil)
                    {
                        // Retain actual accepted bytes so a later stop command
                        // cannot hide either pulse, including a right-only test.
                        var left = report[6]; var right = report[5];
                        var command = left | (right << 8);
                        var elapsed = seconds - testStarted;
                        if (elapsed < RumbleSession.StrengthTestPulseSeconds) Volatile.Write(ref testFirstCommand, command);
                        else if (elapsed >= RumbleSession.StrengthTestPulseSeconds + RumbleSession.StrengthTestGapSeconds)
                            Volatile.Write(ref testSecondCommand, command);
                        var range = Volatile.Read(ref testMotorRange);
                        var low = left == 0 ? right : right == 0 ? left : Math.Min(left, right);
                        var high = Math.Max(left, right);
                        var minimum = testWrites == 0 ? low : Math.Min((byte)range, low);
                        var maximum = testWrites == 0 ? high : Math.Max((byte)(range >> 8), high);
                        Volatile.Write(ref testMotorRange, minimum | (maximum << 8));
                        Interlocked.Increment(ref testWrites);
                    }
                }
                if (previous.Lighting.HasValue && desiredLighting == null) Interlocked.Increment(ref releaseCount);
                previous.Lighting = desiredLighting;
                previous.Motors = motors;
                previous.Seconds = seconds;
                sent[path] = previous;
                Interlocked.Increment(ref writeCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation can race successful delivery. Retain possible
                // ownership so the worker's final release still stops that output.
                previous.Lighting ??= lights;
                if (motors != default) previous.Motors = motors;
                previous.Seconds = double.NegativeInfinity;
                sent[path] = previous;
                throw;
            }
            catch (Exception error) { Remove(path); onError(error); }
        }
        Volatile.Write(ref deviceCount, open.Count);
    }

    // Called before backend selection by the same worker as TickAsync.
    public void Discover(bool lighting, bool bluetoothRumble, double seconds)
    {
        bluetoothRequested = bluetoothRumble;
        if ((!lighting && !bluetoothRumble) || !isForeground() || seconds < nextScan) return;
        nextScan = seconds + 1;
        try { Scan(); }
        catch (Exception error) { Dispose(); nextScan = seconds + 1; onError(error); }
    }

    public bool IsConnected(int slot) => slot == 0 && bluetoothPath != null && isForeground();
    public bool SetVibration(int slot, RumbleMotors motors) => SetVibration(slot, motors, default);
    public bool SetVibration(int slot, RumbleMotors motors, RumbleMotors screen)
    {
        if (slot != 0 || bluetoothPath == null) return false;
        bluetoothMotors = isForeground() ? RumbleMotors.Merge(default, motors, screen) : default;
        return true;
    }

    private void Scan()
    {
        var paths = new HashSet<string>(devices.GetPaths(), StringComparer.OrdinalIgnoreCase);
        foreach (var path in open.Keys.Where(path => !paths.Contains(path)).ToArray()) Remove(path);
        foreach (var path in paths)
        {
            if (open.ContainsKey(path)) continue;
            if (!isForeground()) break;
            var device = devices.Open(path);
            if (device != null) open.Add(path, device);
        }
        PublishDevices();
    }

    private void PublishDevices()
    {
        var selected = bluetoothPath != null && open.ContainsKey(bluetoothPath) ? bluetoothPath :
            open.FirstOrDefault(p => p.Value.ReportLength == DualSenseReport.BluetoothLength).Key;
        if (selected != bluetoothPath)
        {
            bluetoothPath = selected;
            bluetoothMotors = default;
            Volatile.Write(ref bluetoothCommand, 0);
            Volatile.Write(ref testMotorRange, 0); Volatile.Write(ref testWrites, 0);
            Volatile.Write(ref testFirstCommand, 0); Volatile.Write(ref testSecondCommand, 0);
            testStarted = double.NegativeInfinity;
            testUntil = double.NegativeInfinity;
            Interlocked.Increment(ref bluetoothGeneration);
        }
        Volatile.Write(ref bluetoothImproved, selected != null && open[selected].ImprovedRumble ? 1 : 0);
        Volatile.Write(ref usbCount, open.Values.Count(d => DualSenseReport.IsUsbLength(d.ReportLength)));
        Volatile.Write(ref bluetoothCount, open.Values.Count(d => d.ReportLength == DualSenseReport.BluetoothLength));
        Volatile.Write(ref deviceCount, open.Count);
    }

    private void Remove(string path)
    {
        if (open.Remove(path, out var device)) device.Dispose();
        sent.Remove(path);
        PublishDevices();
    }

    public async Task ReleaseAsync(bool allowForeground, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var path in open.Keys.ToArray())
            {
                // Clear only the light/rumble fields this session actually wrote.
                if (!sent.TryGetValue(path, out var previous)) continue;
                if (!allowForeground && isForeground()) break;
                var lights = previous.Lighting.HasValue ? new LightingFrame(0, 0, 0, 0) : (LightingFrame?)null;
                var motors = previous.Motors != default ? default(RumbleMotors) : (RumbleMotors?)null;
                if (lights == null && motors == null) continue;
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(500);
                    await open[path].WriteAsync(DualSenseReport.Create(open[path].ReportLength, lights, motors,
                        previous.Sequence++, open[path].ImprovedRumble), deadline.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref releaseCount);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error) { onError(error); }
            }
        }
        finally { Dispose(); } // No polling/retries or retained handles after release.
    }

    public void Dispose()
    {
        foreach (var device in open.Values) device.Dispose();
        open.Clear();
        sent.Clear();
        PublishDevices();
        nextScan = 0;
    }
}
