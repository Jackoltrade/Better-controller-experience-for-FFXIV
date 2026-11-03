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
    Task WriteAsync(byte[] report, CancellationToken cancellationToken);
}

public interface ILightingDevices
{
    IReadOnlyCollection<string> GetPaths();
    ILightingDevice? Open(string path);
}

/// <summary>Called sequentially by one worker. No controller inputs are read.</summary>
public sealed class LightingSession : IDisposable
{
    private readonly ILightingDevices devices;
    private readonly Func<bool> isForeground;
    private readonly Action<Exception> onError;
    private readonly Dictionary<string, ILightingDevice> open = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (LightingFrame Frame, double Seconds)> sent = new(StringComparer.OrdinalIgnoreCase);
    private double nextScan;
    private int deviceCount;
    private long writeCount;
    private long releaseCount;

    public int DeviceCount => Volatile.Read(ref deviceCount);
    public long WriteCount => Interlocked.Read(ref writeCount);
    public long ReleaseCount => Interlocked.Read(ref releaseCount);

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
        if (!settings.Enabled || !isForeground())
        {
            await ReleaseAsync(!settings.Enabled, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (seconds >= nextScan)
        {
            nextScan = seconds + 1;
            try { Scan(); }
            catch (Exception error) { Dispose(); nextScan = seconds + 1; onError(error); return; }
        }

        var frame = LightingEffects.Render(settings, seconds, sfxLevel, limitBreak);
        foreach (var path in open.Keys.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Check again after discovery and before every write, including multi-controller setups.
            if (!isForeground()) { await ReleaseAsync(false, cancellationToken).ConfigureAwait(false); return; }
            // A slow foreground refresh also recovers from another app's one-shot reconnect update.
            if (sent.TryGetValue(path, out var previous) && previous.Frame == frame && seconds - previous.Seconds < 1) continue;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(500);
                await open[path].WriteAsync(DualSenseReport.Create(open[path].ReportLength, frame), deadline.Token).ConfigureAwait(false);
                sent[path] = (frame, seconds);
                Interlocked.Increment(ref writeCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) { Remove(path); onError(error); }
        }
        Volatile.Write(ref deviceCount, open.Count);
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
        Volatile.Write(ref deviceCount, open.Count);
    }

    private void Remove(string path)
    {
        if (open.Remove(path, out var device)) device.Dispose();
        sent.Remove(path);
    }

    public async Task ReleaseAsync(bool allowForeground, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var path in open.Keys.ToArray())
            {
                // Clear only controllers whose lighting this session changed.
                if (!sent.ContainsKey(path)) continue;
                if (!allowForeground && isForeground()) break;
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(500);
                    await open[path].WriteAsync(DualSenseReport.Create(open[path].ReportLength, new LightingFrame(0, 0, 0, 0)), deadline.Token).ConfigureAwait(false);
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
        Volatile.Write(ref deviceCount, 0);
        nextScan = 0;
    }
}
