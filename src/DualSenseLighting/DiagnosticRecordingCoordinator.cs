// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;
namespace DualSenseLighting;

internal sealed class DiagnosticRecordingCoordinator
{
    private readonly string directory, cameraPath;
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    private readonly Func<SfxAudioCapture?> capture;
    private readonly string PluginVersion;
    private readonly Stopwatch clock;
    private bool disposed;
    private SfxTrace? sfxTrace;
    private Task? traceSave;
    private bool traceDeleting;
    private int traceDeleteRequested;
    private readonly Stopwatch traceClock = new();
    private double nextTraceSizeRefresh;
    private long? traceDiskBytes;
    private string TracePath => System.IO.Path.Combine(directory, "sfx-trace.json");
    private string TraceDirectory => TraceArchive.ArchiveDirectory(TracePath);
    private DateTimeOffset traceStarted;
    private int traceSegment;
    private bool traceStopRequested;
    private int traceToggleRequested;
    private string? traceStatus;
    private string? traceError;

    internal DiagnosticRecordingCoordinator(string directory, string cameraPath, Configuration configuration, IPluginLog log,
        Func<SfxAudioCapture?> capture, string version, Stopwatch clock)
    { this.directory = directory; this.cameraPath = cameraPath; this.configuration = configuration; this.log = log; this.capture = capture; PluginVersion = version; this.clock = clock; }
    internal bool Active => sfxTrace != null;
    internal bool Busy => sfxTrace != null || traceSave != null || Volatile.Read(ref traceDeleteRequested) != 0 || Volatile.Read(ref traceToggleRequested) != 0;
    internal long? DiskBytes => traceDiskBytes;
    internal double Elapsed => traceClock.Elapsed.TotalSeconds;
    internal string? Status => traceStatus;
    internal string? Error => traceError;
    internal string Path => TracePath;
    internal string ArchiveDirectory => TraceDirectory;
    internal string CameraPath => cameraPath;
    internal void RequestToggle() => Interlocked.Increment(ref traceToggleRequested);
    internal void RequestDelete() => Interlocked.Exchange(ref traceDeleteRequested, 1);
    internal void Update()
    {
        CompleteTraceSave();
        if (Interlocked.Exchange(ref traceDeleteRequested, 0) != 0) DeleteRecordings();
        if ((Interlocked.Exchange(ref traceToggleRequested, 0) & 1) != 0) ToggleTrace();
    }
    internal void Sample(SfxAudioCapture capture, HapticSettings haptics, IObjectTable objects)
    {
            if (sfxTrace != null)
            {
                try
                {
                    var segmentComplete = sfxTrace.Sample(capture, haptics, objects, traceStopRequested);
                    if (traceStopRequested) StopTrace();
                    else if (segmentComplete)
                    {
                        // At most one completed segment and one active segment
                        // are retained in memory. A stalled disk cannot grow a queue.
                        if (traceSave != null)
                        {
                            StopTrace();
                            traceStatus = "Storage is slow. Recording stopped; waiting for saved files.";
                        }
                        else
                        {
                            QueueTraceSave(sfxTrace);
                            sfxTrace = new SfxTrace(PluginVersion, traceStarted, ++traceSegment);
                            capture.SetTracing(true);
                        }
                    }
                }
                catch (Exception error)
                {
                    StopTrace();
                    traceStatus = "Recording stopped: {0}";
                    traceError = error.Message;
                    log.Warning(error, "SFX diagnostic recording stopped.");
                }
            }
    }
    internal void Shutdown()
    {
        disposed = true;
        ResourceCleanup.Run(StopTrace, () => capture()?.SetTracing(false),
            () => traceSave?.GetAwaiter().GetResult());
    }
    internal void ToggleTrace()
    {
        if (sfxTrace != null) { traceStopRequested = true; return; }
        if (!configuration.CanRecordDiagnostics()) { return; }
        CompleteTraceSave();
        if (traceSave != null) return;
        traceError = null;
        var currentCapture = capture();
        if (currentCapture == null) { traceStatus = "Select an SFX effect or enable haptics, then wait for capture to be ready."; return; }
        traceStarted = DateTimeOffset.UtcNow;
        traceClock.Restart();
        nextTraceSizeRefresh = 0;
        traceDiskBytes = null;
        traceSegment = 1;
        sfxTrace = new SfxTrace(PluginVersion, traceStarted, traceSegment);
        currentCapture.SetTracing(true);
        traceStatus = null;
        log.Information("SFX diagnostic recording started; use /bcef trace again to stop.");
    }

    private void QueueTraceSave(SfxTrace recording)
    {
        recording.Stop();
        var path = TracePath;
        var previous = traceSave;
        traceSave = Task.Run(async () =>
        {
            Exception? previousError = null;
            // A failed earlier publication must not prevent saving the final
            // partial segment (for example when the user stops during a write).
            if (previous != null)
            {
                try { await previous.ConfigureAwait(false); }
                catch (Exception error) { previousError = error; }
            }
            recording.Save(path);
            log.Information("SFX diagnostic segment saved: {Path}", path);
            if (previousError != null)
                throw new AggregateException("An earlier SFX diagnostic segment could not be saved.", previousError);
        });
    }

    internal void StopTrace()
    {
        var recording = sfxTrace;
        if (recording == null) return;
        traceStopRequested = false;
        sfxTrace = null;
        traceClock.Stop();
        capture()?.SetTracing(false);
        QueueTraceSave(recording);
        traceStatus = "Saving recording...";
    }

    internal void CompleteTraceSave()
    {
        if (traceSave == null || !traceSave.IsCompleted) return;
        var completed = traceSave;
        var deleting = traceDeleting;
        traceSave = null;
        traceDeleting = false;
        try
        {
            completed.GetAwaiter().GetResult();
            if (sfxTrace == null && traceError == null) traceStatus = deleting ? "Recordings deleted." : "Recording saved.";
        }
        catch (Exception error)
        {
            // Stop and attempt the active partial segment independently. The
            // previous error remains visible even if this final write succeeds.
            if (!deleting) StopTrace();
            traceStatus = deleting ? "Could not delete recordings: {0}" : "Recording stopped: {0}";
            traceError = error.Message;
            log.Warning(error, deleting ? "Diagnostic recordings could not be deleted." : "SFX diagnostic recording could not be saved.");
        }
        if (deleting) nextTraceSizeRefresh = 0;
    }

    internal void RefreshTraceSize()
    {
        var seconds = clock.Elapsed.TotalSeconds;
        if (seconds < nextTraceSizeRefresh) return;
        nextTraceSizeRefresh = seconds + 1;
        try
        {
            traceDiskBytes = TraceArchive.RecordingFiles(TracePath)
                .Concat(TraceArchive.RecordingFiles(cameraPath))
                .Distinct(StringComparer.OrdinalIgnoreCase).Sum(path => new FileInfo(path).Length);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Saving, retention or manual cleanup can race this display-only check.
            traceDiskBytes = null;
        }
    }

    internal void DeleteRecordings()
    {
        if (disposed) return;
        if (sfxTrace != null || traceSave != null)
        { traceStatus = "Stop recording and wait for saves to finish."; return; }
        // The camera recorder owns a separate save task. Stop it before taking
        // the task snapshot, then reserve our save queue so a new trace cannot
        // start while either writer is finishing or files are being removed.
        var currentCapture = capture();
        currentCapture?.SetTracing(false);
        var cameraSave = currentCapture?.TraceSaveCompletion ?? Task.CompletedTask;
        var sfxPath = TracePath;
        traceDeleting = true;
        traceError = null;
        traceStatus = "Deleting recordings...";
        traceSave = Task.Run(async () =>
        {
            try { await cameraSave.ConfigureAwait(false); }
            catch (Exception error) { log.Warning(error, "Camera timing save failed before recording cleanup."); }
            TraceArchive.DeleteRecordings(sfxPath, cameraPath);
        });
    }
}
