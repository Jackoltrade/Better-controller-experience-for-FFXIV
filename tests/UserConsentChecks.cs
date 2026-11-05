// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DualSenseLighting;
using DualSenseLighting.Core;
using DualSenseLighting.Ui;
using DualSenseLighting.Windows;

internal static class UserConsentChecks
{
    private sealed class Devices : ILightingDevices
    {
        public int Scans;
        public IReadOnlyCollection<string> GetPaths() { Scans++; return ["unexpected"]; }
        public ILightingDevice Open(string path) => throw new Exception("Controller opened before setup");
    }

    internal static async Task Run(Action<bool, string> check)
    {
        var config = new Configuration { HasSeenGeneralInfo = true, FootstepAmplifier = true, HapticHitReactions = true };
        check(!config.HasCompletedSetup && !config.HasAcknowledgedRecording && !config.CanRecordDiagnostics(),
            "Viewing the old information guide silently granted setup or recording consent");
        check(!config.Snapshot(requireSetup: true).Enabled && !config.HapticsSnapshot(requireSetup: true).NeedsAudio,
            "Saved default-on controller features became active before explicit setup");
        config.SetFeature(ControllerFeature.Haptics, FeatureSwitch.On);
        config.ResetHapticsToDefaults(); config.ResetLightingToDefaults();
        check(!config.HasCompletedSetup && !config.Snapshot(requireSetup: true).Enabled &&
            !config.HapticsSnapshot(requireSetup: true).NeedsAudio,
            "Commands or resetting settings bypassed setup consent");

        var devices = new Devices();
        using (var session = new LightingSession(devices, () => true, e => throw e))
        {
            var lighting = config.Snapshot(requireSetup: true);
            var haptics = config.HapticsSnapshot(requireSetup: true);
            session.Discover(lighting.Enabled, haptics.NeedsAudio, 0);
            await session.TickAsync(lighting, 0, default);
            check(devices.Scans == 0 && session.WriteCount == 0, "Pending setup accessed controller devices");
            CheckNativeDisabled(haptics, check);
        }

        foreach (var lighting in new[] { false, true })
        foreach (var haptics in new[] { false, true })
        {
            config = new Configuration { Language = UiLanguage.Japanese, Red = 42, BluetoothMotorBalance = 0.72f,
                FootstepAmplifier = true, HapticHitReactions = true };
            config.CompleteSetup(lighting, haptics);
            check(config.HasCompletedSetup && config.HasSeenGeneralInfo && config.Enabled == lighting && config.HapticEnabled == haptics &&
                config.Snapshot(requireSetup: true).Enabled == lighting && config.HapticsSnapshot(requireSetup: true).NeedsAudio == haptics,
                "Setup did not apply the independently selected lighting and haptic choices");
            check(config.Language == UiLanguage.Japanese && config.Red == 42 && config.BluetoothMotorBalance == 0.72f &&
                config.FootstepAmplifier && config.HapticHitReactions && !config.HasAcknowledgedRecording && !config.CanRecordDiagnostics(),
                "Setup changed unrelated saved choices or enabled diagnostic recording");
            var reloaded = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;
            check(reloaded.HasCompletedSetup && reloaded.Snapshot(requireSetup: true) == config.Snapshot(requireSetup: true) &&
                reloaded.HapticsSnapshot(requireSetup: true) == config.HapticsSnapshot(requireSetup: true),
                "Reloading lost setup consent or changed its selected effects");
        }
        foreach (var setup in new[] { false, true })
        foreach (var recording in new[] { false, true })
        {
            config = new Configuration { HasCompletedSetup = setup, HasAcknowledgedRecording = recording };
            check(config.CanRecordDiagnostics() == (setup && recording), "Recording bypassed setup or its own notice");
            config.ResetHapticsToDefaults(); config.ResetLightingToDefaults();
            var reloaded = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;
            check(reloaded.HasCompletedSetup == setup && reloaded.HasAcknowledgedRecording == recording &&
                reloaded.CanRecordDiagnostics() == (setup && recording),
                "Reset/reload cleared or silently granted a consent choice");
        }
        var legacy = JsonSerializer.Deserialize<Configuration>("{\"HasSeenGeneralInfo\":true,\"Enabled\":true,\"HapticEnabled\":true}")!;
        check(legacy.Enabled && legacy.HapticEnabled && !legacy.HasCompletedSetup && !legacy.CanRecordDiagnostics() &&
            !legacy.Snapshot(requireSetup: true).Enabled && !legacy.HapticsSnapshot(requireSetup: true).NeedsAudio,
            "Legacy information-viewed state was treated as explicit consent");
        var screenPath = TraceArchive.ScreenTimingPath;
        check(screenPath == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DualSenseLighting", "screen-haptics-timing.json") &&
            TraceArchive.ArchiveDirectory(screenPath) == Path.Combine(Path.GetDirectoryName(screenPath)!, "sfx-traces"),
            "The disclosed camera-recording paths differ from the canonical storage paths");
    }

    private static unsafe void CheckNativeDisabled(HapticSettings settings, Action<bool, string> check)
    {
        var native = new NativeHapticOutput(null, 0, 0, 0);
        native.Configure(settings, true, true, true, true, true);
        check(!native.Active && !native.Prepare(null, true, 2, 0).HasOutput && !native.RumbleSignal.Active,
            "Pending setup activated game audio or added haptic output");
    }
}
