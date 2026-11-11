// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Reflection;
using System.Collections;
using System.Linq;

// Both flavors exercise the same functional adapters. This explicitly verifies
// absent diagnostic capabilities rather than treating missing reflection as a skip.
internal static class ProductionBoundaryChecks
{
    internal static bool Diagnostics(Assembly plugin) =>
        plugin.GetType("DualSenseLighting.DiagnosticRecordingCoordinator") != null;

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        var diagnostics = Diagnostics(plugin);
        foreach (var name in new[] { "DiagnosticRecordingCoordinator", "Core.TraceArchive", "Core.NpcSpawnMetadata",
            "Core.UpdateTiming", "Windows.SfxNativeTrace", "Windows.SfxTrace", "Windows.NpcSpawnObserver",
            "Windows.ScreenEffectSourceObserver", "Core.ScreenEffectSourceRecording", "Core.GameAudioLayout+SoundReservation" })
            check((plugin.GetType("DualSenseLighting." + name) != null) == diagnostics,
                "Compiled diagnostic type boundary differs: " + name);
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var (owner, names) in new[] {
            ("Plugin", new[] { "recording", "updateTiming" }),
            ("Core.ScreenEffectHaptics", new[] { "MinimumHeadroom", "LimitedFrames" }),
            ("Core.GameAudioLayout+PlayerSoundOwnership", new[] { "Snapshot", "EnemyActors", "origins", "effectOwnerGetters", "sourceActors", "sourceDraws", "timelineOwners" }),
            ("Windows.SfxAudioCapture", new[] { "tracing", "traceRoots", "traceTimes", "tracePeaks", "SetTracing" }),
            ("Windows.GameScreenEffects", new[] { "timing", "traceSave", "motionPreparationTicks", "SetTracing" }),
            ("Windows.SecondaryMotionSession", new[] { "Recording" }),
            ("Ui.SettingsUi", new[] { "DiagnosticsAvailable", "RequestTraceToggle", "DrawFrameworkTiming" }),
            ("Ui.UiRuntimeState", new[] { "MeasureUpdates", "Timing" }),
            ("Configuration", new[] { "HasAcknowledgedRecording", "CanRecordDiagnostics" }),
            ("Core.PluginCommandKind", new[] { "Trace" }),
        })
        {
            var type = plugin.GetType("DualSenseLighting." + owner, true)!;
            foreach (var name in names)
                check((type.GetMember(name, all).Length != 0) == diagnostics,
                    "Compiled diagnostic member boundary differs: " + owner + "." + name);
        }
        var translations = (IDictionary)plugin.GetType("DualSenseLighting.Ui.UiText", true)!
            .GetField("Translations", all)!.GetValue(null)!;
        foreach (var key in new[] { "Measure framework updates", "Start SFX recording", "SFX recordings" })
            check(translations.Contains(key) == diagnostics,
                "Compiled translation flavor differs: " + key);
        foreach (var (owner, count) in new[] { ("GameScreenEffects", 3), ("SfxAudioCapture", 4) })
        {
            var constructor = plugin.GetType("DualSenseLighting.Windows." + owner, true)!.GetConstructors().Single();
            var parameters = constructor.GetParameters();
            check(parameters.Length == count + (diagnostics ? 1 : 0) &&
                (!diagnostics || parameters[^1].Name == "developmentInstall" && parameters[^1].ParameterType == typeof(bool)),
                "Compiled observer constructor has wrong flavor signature: " + owner);
        }
        check(plugin.GetType("DualSenseLighting.Core.SecondaryVectorSample") != null &&
            plugin.GetType("DualSenseLighting.Windows.SecondaryMotionObserver") != null,
            "Production flavor removed functional screen filtering");
    }
}
