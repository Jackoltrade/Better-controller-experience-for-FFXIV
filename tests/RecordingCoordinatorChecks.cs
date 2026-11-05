// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

internal static class RecordingCoordinatorChecks
{
    public class QuietLog : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => null;
    }

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        var directory = Path.Combine(Path.GetTempPath(), "record-coordinator-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var type = plugin.GetType("DualSenseLighting.DiagnosticRecordingCoordinator", true)!;
        var configType = plugin.GetType("DualSenseLighting.Configuration", true)!;
        var captureType = plugin.GetType("DualSenseLighting.Windows.SfxAudioCapture", true)!;
        var logType = Type.GetType("Dalamud.Plugin.Services.IPluginLog, Dalamud", true)!;
        var config = Activator.CreateInstance(configType)!;
        var capture = RuntimeHelpers.GetUninitializedObject(captureType);
        var captureGetter = Expression.Lambda(typeof(Func<>).MakeGenericType(captureType), Expression.Constant(capture, captureType)).Compile();
        var log = DispatchProxy.Create(logType, typeof(QuietLog));
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0];
        var coordinator = constructor.Invoke(new object[] { directory, Path.Combine(directory, "screen-haptics-timing.json"), config, log, captureGetter, "test", Stopwatch.StartNew() });
        object? Call(string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(coordinator, null);
        bool Active() => (bool)type.GetProperty("Active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
        void Set(string name, object? value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(coordinator, value);
        try
        {
            Call("RequestToggle"); Call("Update");
            check(!Active(), "Recorder started before explicit recording consent");
            configType.GetProperty("HasCompletedSetup")!.SetValue(config, true);
            configType.GetProperty("HasAcknowledgedRecording")!.SetValue(config, true);
            Call("RequestToggle"); Call("RequestToggle"); Call("Update");
            check(!Active(), "Two queued recording toggles did not cancel");
            var blocked = new TaskCompletionSource();
            Set("traceSave", blocked.Task);
            Call("RequestToggle"); Call("Update");
            check(!Active(), "Recorder restarted while its save queue was reserved");
            blocked.SetResult(); Call("Update"); Call("RequestToggle"); Call("Update");
            check(Active(), "Consented recording did not start with a ready capture");
            Call("RequestDelete"); Call("Update");
            check(Active(), "Deleting while recording disrupted the active segment");
            Set("traceSave", Task.FromException(new IOException("earlier segment failed")));
            Call("CompleteTraceSave");
            check(!Active(), "An earlier save failure left recording running");
            Call("Shutdown");
            check(File.Exists(Path.Combine(directory, "sfx-trace.json")), "Earlier segment failure discarded the final partial recording");
            var error = (string?)type.GetProperty("Error", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator);
            check(error == "earlier segment failed", "Successful final partial save hid the earlier recording failure");
            var cameraPath = Path.Combine(directory, "screen-haptics-timing.json");
            File.WriteAllText(cameraPath, "camera fixture");
            var screenType = plugin.GetType("DualSenseLighting.Windows.GameScreenEffects", true)!;
            var screen = RuntimeHelpers.GetUninitializedObject(screenType);
            var cameraSave = new TaskCompletionSource();
            screenType.GetField("traceSave", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(screen, cameraSave.Task);
            captureType.GetField("screenEffects", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(capture, screen);
            coordinator = constructor.Invoke(new object[] { directory, cameraPath, config, log, captureGetter, "test", Stopwatch.StartNew() });
            Call("RequestDelete"); Call("Update");
            var deleting = (Task)type.GetField("traceSave", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
            check(!deleting.IsCompleted && File.Exists(cameraPath), "Recording deletion did not await the camera writer");
            Call("RequestToggle"); Call("Update");
            check(!Active(), "Recording restarted while deletion awaited a camera writer");
            cameraSave.SetResult(); deleting.GetAwaiter().GetResult(); Call("Update");
            check(!File.Exists(cameraPath) && !File.Exists(Path.Combine(directory, "sfx-trace.json")),
                "Recording cleanup did not delete both completed writers' files");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
