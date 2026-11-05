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
        public bool NeedsMotorSync;
        public bool MotorRequested;
    }
    private readonly Dictionary<string, OutputState> sent = new(StringComparer.OrdinalIgnoreCase);
    // Reserve before claiming output. Failed or absent paths never evict an
    // obligation; capacity exhaustion pauses new ownership instead.
    private const int OwnershipCapacity = 32;
    private sealed class Ownership
    {
        public bool Lighting, Motors, NeedsClear;
    }
    private readonly Dictionary<string, Ownership> owned = new(StringComparer.OrdinalIgnoreCase);
    private bool releasePending;
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
            if (open.Count != 0 || releasePending)
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
            var previous = sent[path];
            try
            {
                if (owned.TryGetValue(path, out var cleanup) && cleanup.NeedsClear)
                {
                    await ClearAsync(path, cleanup, cancellationToken).ConfigureAwait(false);
                    previous.Lighting = null; previous.Motors = default;
                    previous.Seconds = double.NegativeInfinity;
                }
                if (bluetooth && previous.NeedsMotorSync && previous.MotorRequested)
                {
                    await WriteAsync(path, null, default(RumbleMotors), cancellationToken).ConfigureAwait(false);
                    previous.NeedsMotorSync = false;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { MarkUncertain(path); throw; }
            catch (Exception error) { Remove(path); Report(error); continue; }
            if (!isForeground()) { await ReleaseAsync(false, cancellationToken).ConfigureAwait(false); return; }
            // Static light/rumble refresh also recovers another app's one-shot update.
            if (previous.Lighting == desiredLighting && previous.Motors == motors &&
                (desiredLighting == null && motors == default || seconds - previous.Seconds < 1)) continue;
            var lights = desiredLighting ?? (previous.Lighting.HasValue ? new LightingFrame(0, 0, 0, 0) : (LightingFrame?)null);
            var vibration = bluetooth && (motors != default || previous.Motors != default) ? motors : (RumbleMotors?)null;
            if (lights == null && vibration == null) continue;
            try
            {
                if ((desiredLighting.HasValue || motors != default) && !Reserve(path, desiredLighting.HasValue, motors != default)) continue;
                var report = await WriteAsync(path, lights, vibration, cancellationToken).ConfigureAwait(false);
                Acknowledge(path, desiredLighting == null && lights.HasValue, vibration.HasValue && motors == default);
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
                MarkUncertain(path);
                throw;
            }
            catch (Exception error) { Remove(path); Report(error); }
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
        catch (Exception error) { Report(error); } // A failed snapshot proves no device disconnected.
    }

    public bool IsConnected(int slot) => slot == 0 && bluetoothPath != null && isForeground();
    public bool SetVibration(int slot, RumbleMotors motors) => SetVibration(slot, motors, default);
    public bool SetVibration(int slot, RumbleMotors motors, RumbleMotors screen)
    {
        if (slot != 0 || bluetoothPath == null) return false;
        sent[bluetoothPath].MotorRequested = true;
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
            try
            {
                var device = devices.Open(path);
                if (device != null)
                {
                    open.Add(path, device);
                    sent[path] = new OutputState { NeedsMotorSync = device.ReportLength == DualSenseReport.BluetoothLength };
                }
            }
            catch (Exception error) { Report(error); }
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
        MarkUncertain(path);
        if (open.Remove(path, out var device)) Close(device);
        sent.Remove(path);
        PublishDevices();
    }

    private void Report(Exception error)
    {
        // Diagnostics must never interrupt another device's release/retirement.
        try { onError(error); } catch { }
    }

    private void Close(ILightingDevice device)
    {
        try { device.Dispose(); } catch (Exception error) { Report(error); }
    }

    private bool Reserve(string path, bool lighting, bool motors)
    {
        if (!owned.TryGetValue(path, out var ownership))
        {
            if (owned.Count >= OwnershipCapacity)
            {
                Report(new InvalidOperationException("Controller output paused: pending cleanup capacity is full; reconnect earlier controllers to clear their output."));
                return false;
            }
            owned.Add(path, ownership = new Ownership());
        }
        ownership.Lighting |= lighting;
        ownership.Motors |= motors;
        releasePending = true;
        return true;
    }

    private void MarkUncertain(string path)
    {
        if (owned.TryGetValue(path, out var ownership)) { ownership.NeedsClear = true; releasePending = true; }
    }

    private void Acknowledge(string path, bool lighting, bool motors)
    {
        if (!owned.TryGetValue(path, out var ownership)) return;
        if (lighting) ownership.Lighting = false;
        if (motors) ownership.Motors = false;
        if (!ownership.Lighting && !ownership.Motors) owned.Remove(path);
    }

    private async ValueTask<byte[]> WriteAsync(string path, LightingFrame? lights, RumbleMotors? motors, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = open[path];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(500);
        var report = DualSenseReport.Create(device.ReportLength, lights, motors, sent[path].Sequence++, device.ImprovedRumble);
        await device.WriteAsync(report, deadline.Token).ConfigureAwait(false);
        return report;
    }

    private async Task ClearAsync(string path, Ownership ownership, CancellationToken cancellationToken)
    {
        await WriteAsync(path, ownership.Lighting ? new LightingFrame(0, 0, 0, 0) : null,
            ownership.Motors ? default(RumbleMotors) : null, cancellationToken).ConfigureAwait(false);
        CompleteClear(path, ownership);
    }

    private void CompleteClear(string path, Ownership ownership)
    {
        if (ownership.Motors)
        {
            sent[path].NeedsMotorSync = false;
            if (path == bluetoothPath) Volatile.Write(ref bluetoothCommand, 0);
        }
        Acknowledge(path, ownership.Lighting, ownership.Motors);
        Interlocked.Increment(ref releaseCount);
    }

    public async Task ReleaseAsync(bool allowForeground, CancellationToken cancellationToken)
    {
        // Launch independent stop writes before waiting. A stalled controller
        // cannot spend every other controller's deadline before their stop starts.
        var writes = new List<(string Path, Ownership Ownership, Task<Exception?> Result)>();
        try
        {
            foreach (var path in owned.Keys.ToArray())
            {
                // Clear only the light/rumble fields this session actually wrote.
                if (!allowForeground && isForeground()) break;
                try
                {
                    MarkUncertain(path);
                    if (!open.ContainsKey(path))
                    {
                        var device = devices.Open(path);
                        if (device == null) continue;
                        open.Add(path, device);
                        sent[path] = new OutputState(); // A reopened handle starts a fresh wire sequence.
                    }
                    var ownership = owned[path];
                    writes.Add((path, ownership, TryWriteAsync(path, ownership, cancellationToken)));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception error) { Report(error); }
            }
            await Task.WhenAll(writes.Select(write => write.Result)).ConfigureAwait(false);
            // Only this worker acknowledges ownership; no continuation mutates
            // the dictionaries while another device is still writing.
            foreach (var write in writes)
            {
                var error = await write.Result.ConfigureAwait(false);
                if (error == null) CompleteClear(write.Path, write.Ownership);
                else if (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested) Report(error);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally { Dispose(); releasePending = false; } // Retain metadata, never native handles or retry loops.
    }

    private async Task<Exception?> TryWriteAsync(string path, Ownership ownership, CancellationToken cancellationToken)
    {
        try
        {
            await WriteAsync(path, ownership.Lighting ? new LightingFrame(0, 0, 0, 0) : null,
                ownership.Motors ? default(RumbleMotors) : null, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception error) { return error; }
    }

    public void Dispose()
    {
        foreach (var path in open.Keys) MarkUncertain(path);
        foreach (var device in open.Values) Close(device);
        open.Clear();
        sent.Clear();
        PublishDevices();
        nextScan = 0;
    }
}
