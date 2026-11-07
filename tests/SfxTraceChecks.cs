// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

internal static class SfxTraceChecks
{
    private static readonly DateTimeOffset Session =
        new DateTimeOffset(2026, 11, 4, 12, 34, 56, TimeSpan.FromHours(8)).AddTicks(1234567);
    private const string Version = "sfx-trace-segment-check";

    internal static void Run(Action<bool, string> check, string pluginPath, string dalamudPath)
    {
        // The compiled plugin supplies its own internal record types. Loading it
        // does not construct Plugin, audio hooks, controllers, or game services.
        var dependencyDirectory = Path.GetFullPath(dalamudPath);
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            var path = Path.Combine(dependencyDirectory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(pluginPath));
            ImpactAdapterChecks.Run(check, assembly);
            RegularEffectAdapterChecks.Run(check, assembly);
            GameTargetRelationChecks.Run(check, assembly);
            WindowingChecks.Run(check, assembly);
            SpecialSoundPlaybackChecks.Run(check, assembly);
            ArenaCatalogLifecycleChecks.Run(check, assembly);
            HookOriginalLifecycleChecks.Run(check, assembly);
            OwnerLifecycleChecks.Run(check, assembly);
            ScreenObserverActivationChecks.Run(check, assembly);
            RecordingCoordinatorChecks.Run(check, assembly);
            SettingsConsentChecks.Run(check, assembly);
            GameObjectLayoutChecks.CheckInstalledLayout(check, assembly);
            var harness = new Harness(assembly);
            var boundary = CheckSegmentBoundary(check, harness);
            var final = CheckFinalSample(check, harness);
            var bounded = CheckFrameBound(check, harness);
            CheckSavedSegments(check, harness, boundary, final, bounded);
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }

    private static object CheckSegmentBoundary(Action<bool, string> check, Harness harness)
    {
        var trace = harness.NewTrace(2);
        harness.SetElapsed(trace, 0);
        check(!harness.Sample(trace) && harness.FrameCount(trace) == 1,
            "A fresh trace did not capture its initial frame");
        harness.SetElapsed(trace, 16.25);
        check(!harness.Sample(trace) && harness.FrameCount(trace) == 2,
            "Manual recording still completed or stopped sampling at the old fifteen-second limit");
        harness.SetElapsed(trace, 59.9);
        check(!harness.Sample(trace) && harness.FrameCount(trace) == 3,
            "A trace segment completed before sixty seconds");
        harness.SetElapsed(trace, 60);
        check(harness.Sample(trace) && harness.FrameCount(trace) == 4,
            "The sixty-second boundary did not complete the segment and retain its final snapshot");
        harness.Stop(trace);
        return trace;
    }

    private static object CheckFinalSample(Action<bool, string> check, Harness harness)
    {
        var trace = harness.NewTrace(3);
        harness.SetElapsed(trace, 0);
        harness.Sample(trace);
        harness.SetElapsed(trace, 0.01);
        for (var i = 0; i < 20; i++) harness.Sample(trace);
        check(harness.FrameCount(trace) == 1,
            "Normal trace sampling ignored the fifty-millisecond sampling interval");
        harness.Stop(trace);
        check(!harness.Sample(trace, final: true) && harness.FrameCount(trace) == 2,
            "Manual Stop could not retain a final snapshot inside the sampling interval");
        return trace;
    }

    private static object CheckFrameBound(Action<bool, string> check, Harness harness)
    {
        var trace = harness.NewTrace(4);
        harness.SetElapsed(trace, 1);
        var completedEarly = false;
        // Advancing nextSample bypasses pacing without sleeping or making this
        // test take a minute. The production frame cap must still apply.
        for (var i = 0; i < 1199; i++)
        {
            harness.AllowNextSample(trace);
            completedEarly |= harness.Sample(trace);
        }
        check(!completedEarly && harness.FrameCount(trace) == 1199,
            "Trace capacity completed early or discarded frames before its configured bound");
        harness.AllowNextSample(trace);
        check(harness.Sample(trace) && harness.FrameCount(trace) == 1200,
            "A full trace segment did not complete at its 1,200-frame bound");
        var remainedComplete = true;
        for (var i = 0; i < 32; i++)
        {
            harness.AllowNextSample(trace);
            remainedComplete &= harness.Sample(trace, final: (i & 1) == 0);
        }
        check(remainedComplete && harness.FrameCount(trace) == 1200,
            "Repeated normal or final samples grew a completed segment beyond its frame bound");

        var clock = harness.Clock(trace);
        clock.Start();
        check(clock.IsRunning, "Trace Stop test did not begin with a running clock");
        harness.Stop(trace);
        check(!clock.IsRunning, "Manual Stop left the trace duration running");
        return trace;
    }

    private static void CheckSavedSegments(Action<bool, string> check, Harness harness,
        object boundary, object final, object bounded)
    {
        var root = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,
            "sfx-trace-checks-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            harness.AddArenaSource(boundary);
            var latest = Path.Combine(root, "sfx-trace.json");
            var expected = new[] { (Trace: boundary, Segment: 2, Frames: 4),
                (Trace: final, Segment: 3, Frames: 2), (Trace: bounded, Segment: 4, Frames: 1200) };
            foreach (var item in expected)
            {
                var frozenDuration = harness.Clock(item.Trace).Elapsed.TotalSeconds;
                harness.Save(item.Trace, latest);
                var text = File.ReadAllText(latest);
                using var document = JsonDocument.Parse(text);
                var data = document.RootElement;
                check(data.GetProperty("PluginVersion").GetString() == Version &&
                    data.GetProperty("SessionStartedUtc").GetDateTimeOffset().EqualsExact(Session) &&
                    data.GetProperty("Segment").GetInt32() == item.Segment &&
                    data.GetProperty("StartedUtc").GetDateTimeOffset().EqualsExact(harness.Started(item.Trace)),
                    "A saved segment lost its version, session, sequence number, or capture timestamp");
                check(data.GetProperty("DurationSeconds").GetDouble() == frozenDuration &&
                    harness.Clock(item.Trace).Elapsed.TotalSeconds == frozenDuration,
                    "A stopped segment's duration changed during serialization or file publication");
                var frames = data.GetProperty("Frames");
                check(frames.GetArrayLength() == item.Frames && frames.EnumerateArray().All(frame =>
                    frame.GetProperty("Sounds").GetArrayLength() == 0 &&
                    frame.GetProperty("Voices").GetArrayLength() == 0 &&
                    !frame.GetProperty("Haptics").GetProperty("EnemySkillsReady").GetBoolean()),
                    "Empty-capture frames did not survive JSON serialization without native game state");
                check(!text.Contains('\n') && !text.Contains('\r'),
                    "Trace segments reverted to indented JSON instead of bounded compact output");
                if (item.Segment == 2)
                {
                    check(frames[3].GetProperty("Seconds").GetDouble() >= 60,
                        "The saved completed segment omitted its boundary snapshot");
                    var arena = data.GetProperty("ArenaSources");
                    check(arena.GetArrayLength() == 1 && arena[0].GetProperty("SoundId").GetInt32() == 123 &&
                        arena[0].GetProperty("Resource").GetString() == "test/arena.scd" &&
                        arena[0].GetProperty("SoundNumber").GetUInt32() == 7 &&
                        arena[0].GetProperty("PoolSlot").GetInt32() == 4 &&
                        arena[0].GetProperty("SoundData").GetString() == "1234" &&
                        arena[0].GetProperty("SoundLayout").GetString() == "5678" &&
                        arena[0].GetProperty("InstanceId").GetUInt32() == 9 &&
                        arena[0].GetProperty("SubId").GetUInt32() == 10,
                        "A saved arena source lost its playback, resource, or layout identity");
                    var position = arena[0].GetProperty("Position");
                    check(position.GetProperty("X").GetSingle() == 100 &&
                        position.GetProperty("Y").GetSingle() == 2.5f &&
                        position.GetProperty("Z").GetSingle() == -100,
                        "Arena world coordinates serialized as an empty object instead of X/Y/Z");
                }
                if (item.Segment == 3)
                    check(frames[1].GetProperty("Seconds").GetDouble() is >= 0.01 and < 0.05,
                        "The saved manual Stop snapshot did not preserve its sub-interval timestamp");
            }

            var archives = Directory.GetFiles(Path.Combine(root, "sfx-traces"), "*.json");
            var segments = archives.Select(path =>
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var data = document.RootElement;
                check(data.GetProperty("SessionStartedUtc").GetDateTimeOffset().EqualsExact(Session),
                    "Archived recording segments lost their shared manual-recording session");
                return data.GetProperty("Segment").GetInt32();
            }).ToHashSet();
            check(archives.Length == 3 && segments.SetEquals(new[] { 2, 3, 4 }),
                "Saving consecutive recording segments overwrote an earlier segment");
            check(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(),
                "Segment publication left temporary files behind");
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Harness
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly Type traceType;
        private readonly object capture;
        private readonly MethodInfo sample, stop, save;
        private readonly FieldInfo clock, frames, nextSample, started, elapsed;

        internal Harness(Assembly assembly)
        {
            traceType = assembly.GetType("DualSenseLighting.Windows.SfxTrace", throwOnError: true)!;
            var captureType = assembly.GetType("DualSenseLighting.Windows.SfxAudioCapture", throwOnError: true)!;
            capture = RuntimeHelpers.GetUninitializedObject(captureType);
            foreach (var name in new[] { "traceRoots", "traceTimes", "tracePeaks" })
                Field(captureType, name).SetValue(capture, new long[512]);
            sample = traceType.GetMethod("Sample") ?? throw new MissingMethodException(traceType.FullName, "Sample");
            stop = traceType.GetMethod("Stop") ?? throw new MissingMethodException(traceType.FullName, "Stop");
            save = traceType.GetMethod("Save") ?? throw new MissingMethodException(traceType.FullName, "Save");
            clock = Field(traceType, "clock"); frames = Field(traceType, "frames");
            nextSample = Field(traceType, "nextSample"); started = Field(traceType, "started");
            // Test-only runtime detail: the .NET 10 Stopwatch stores elapsed
            // high-resolution ticks here. Fail clearly if that runtime changes.
            elapsed = Field(typeof(Stopwatch), "_elapsed");
            if (elapsed.FieldType != typeof(long))
                throw new NotSupportedException("SfxTrace checks require the .NET 10 Stopwatch Int64 _elapsed field.");
        }

        private static FieldInfo Field(Type type, string name) => type.GetField(name, PrivateInstance) ??
            throw new MissingFieldException(type.FullName, name);
        internal object NewTrace(int segment) => Activator.CreateInstance(traceType, Version, Session, segment) ??
            throw new InvalidOperationException("Could not construct an isolated SfxTrace segment.");
        internal Stopwatch Clock(object trace) => (Stopwatch)clock.GetValue(trace)!;
        internal DateTimeOffset Started(object trace) => (DateTimeOffset)started.GetValue(trace)!;
        internal int FrameCount(object trace) => ((ICollection)frames.GetValue(trace)!).Count;
        internal bool Sample(object trace, bool final = false) =>
            (bool)sample.Invoke(trace, new object?[] { capture, null, null, final })!;
        internal void Stop(object trace) => stop.Invoke(trace, null);
        internal void Save(object trace, string path) => save.Invoke(trace, new object[] { path });
        internal void AllowNextSample(object trace) => nextSample.SetValue(trace, 0d);
        internal void AddArenaSource(object trace)
        {
            var type = traceType.Assembly.GetType("DualSenseLighting.Core.ArenaEffectSource", throwOnError: true)!;
            var source = Activator.CreateInstance(type, 123, new Vector3(100, 2.5f, -100),
                "test/arena.scd", 7u, 4, "1234", "5678", 9u, 10u)!;
            ((IDictionary)Field(traceType, "arenaSources").GetValue(trace)!)[123] = source;
        }
        internal void SetElapsed(object trace, double seconds)
        {
            var timer = Clock(trace); timer.Stop();
            var ticks = checked((long)Math.Ceiling(seconds * Stopwatch.Frequency));
            elapsed.SetValue(timer, ticks);
            // Elapsed converts native ticks to TimeSpan ticks. Compensate for
            // rounding at an exact boundary without advancing real wall time.
            if (timer.Elapsed.TotalSeconds < seconds)
                elapsed.SetValue(timer, ticks + Math.Max(1, Stopwatch.Frequency / TimeSpan.TicksPerSecond + 1));
        }
    }
}
