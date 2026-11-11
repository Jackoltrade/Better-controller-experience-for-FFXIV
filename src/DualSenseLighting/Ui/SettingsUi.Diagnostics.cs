// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;
namespace DualSenseLighting.Ui;

internal sealed partial class SettingsUi
{
    private readonly DiagnosticRecordingCoordinator recording;
    private readonly Action<bool> measureUpdates;
    internal bool DiagnosticsAvailable { get; }
    private bool TraceFilesBusy => recording.Busy;
    private bool showRecordingNotice, selectDiagnostics;

    private void DrawLimitBreakDiagnostics(bool foreground)
    {
        var state = runtime.LimitBreakState;
        var seconds = clock.Elapsed.TotalSeconds;
        if (settings.PlayerMode != PlayerIndicatorMode.LimitBreak) ImGui.TextDisabled(T("Limit Break mode is not selected."));
        else if (!foreground || !settings.Enabled) ImGui.TextDisabled(T("Limit Break LEDs paused."));
        else if (runtime.LimitBreakError != null) ImGui.TextWrapped(F("Limit Break detection unavailable: {0}", runtime.LimitBreakError));
        else if (runtime.LimitBreak == null) ImGui.TextDisabled(T("Preparing Limit Break detection…"));
        else if (!state.IsFresh(seconds)) ImGui.TextDisabled(T("No active Limit Break gauge. Bottom LEDs are off."));
        else
        {
            var maximum = state.BarUnits * state.BarCount;
            ImGui.ProgressBar(Math.Clamp(state.CurrentUnits / (float)maximum, 0, 1), new Vector2(0, 0), T(state.IsPvP ? F("Your LB: {0:P0}", Math.Min(state.CurrentUnits / (float)state.BarUnits, 1)) :
                    F("Party LB: {0}/{1} bars ready", state.ReadyBars, state.BarCount)));
            if (state.IsFlashing(seconds)) ImGui.TextDisabled(T(state.Casting ? "Casting your Limit Break — fast flash" : "Your Limit Break used — fast flash"));
        }
        if (runtime.LimitBreak?.InstantFlashError != null)
            ImGui.TextWrapped(F("Instant-LB flashing unavailable: {0}. Gauge and cast LEDs remain active.", runtime.LimitBreak.InstantFlashError));
        if (runtime.LimitBreak?.Errors > 0) ImGui.TextDisabled(F("LB observation errors: {0}", runtime.LimitBreak.Errors));
    }

    private void DrawDiagnostics(bool foreground)
    {
        DrawFrameworkTiming();
        var capture = runtime.Capture;
        ImGui.TextDisabled(F("Version {0}", PluginVersion));
        ImGui.TextUnformatted(T(foreground ? "FFXIV active" : "Background protected — custom output paused"));
        ImGui.TextUnformatted(F("Connected controllers: USB {0}; Bluetooth {1}", session.UsbCount, session.BluetoothCount));
        Section("Recording");
        DrawRecordingStorage();
        if (recording.Active)
        {
            ImGui.TextUnformatted(F("Recording SFX: {0:F0}s elapsed", recording.Elapsed));
            if (ImGui.Button(Label("Stop and save recording"))) RequestTraceToggle();
        }
        else
        {
            ImGui.BeginDisabled(capture == null || TraceFilesBusy);
            try
            {
                if (ImGui.Button(Label("Start SFX recording"))) RequestTraceToggle();
            }
            finally { ImGui.EndDisabled(); }
        }
        Help("Use /bcef trace to start or stop. Buffered data appears in the size after saving.");
        if (recording.Status != null) ImGui.TextWrapped(F(recording.Status, recording.Error ?? ""));

        Section("Live status");
        if (ImGui.CollapsingHeader(Label("Lighting output"))) DrawLightingDiagnostics();
        if (ImGui.CollapsingHeader(Label("SFX capture"))) DrawSfxDiagnostics(foreground, capture);
        if (ImGui.CollapsingHeader(Label("Haptics"))) DrawHapticDiagnostics(foreground, capture);
        if (ImGui.CollapsingHeader(Label("Limit Break detection"))) DrawLimitBreakDiagnostics(foreground);
    }

    private void DrawFrameworkTiming()
    {
        var measuring = runtime.MeasureUpdates;
        if (ImGui.Checkbox(Label("Measure framework updates"), ref measuring)) measureUpdates(measuring);
        Help("Elapsed time includes thread pauses. Samples accumulate until reload, up to 1,000,000. Phase maxima may come from different updates.");
        var total = runtime.Timing.ReadTotal();
        if (total.Count == 0) ImGui.TextDisabled(T("Framework timing: not collected"));
        else ImGui.TextUnformatted(F("Framework updates: {0}; average {1:F3} ms; maximum {2:F3} ms",
            total.Count, total.TotalTicks * 1000d / Stopwatch.Frequency / total.Count,
            total.MaxTicks * 1000d / Stopwatch.Frequency));
        if (total.Count == 0 || !ImGui.CollapsingHeader(Label("Timing breakdown"))) return;
        ImGui.TextUnformatted(F("Updates ≥1 ms: {0}; ≥5 ms: {1}; ≥16.7 ms: {2}",
            total.AtLeast1Ms, total.AtLeast5Ms, total.AtLeast16_7Ms));
        if (!ImGui.BeginTable("##frameworkPhases", 4, ImGuiTableFlags.SizingStretchProp)) return;
        try
        {
            ImGui.TableSetupColumn(T("Phase"), ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableSetupColumn(T("Samples"));
            ImGui.TableSetupColumn(T("Average (ms)"));
            ImGui.TableSetupColumn(T("Maximum (ms)"));
            ImGui.TableHeadersRow();
            for (var index = 0; index < UpdateTiming.PhaseCount; index++)
            {
                var phase = (UpdateTiming.Phase)index;
                var sample = runtime.Timing.ReadPhase(phase);
                var name = phase switch
                {
                    UpdateTiming.Phase.RecordingCoordination => "Input / recording",
                    UpdateTiming.Phase.Observers => "Audio / LB observers",
                    UpdateTiming.Phase.PlayerPublication => "Player snapshot",
                    UpdateTiming.Phase.RosterArena => "Actors / arena",
                    UpdateTiming.Phase.ScreenEffects => "Screen effects",
                    UpdateTiming.Phase.RecordingSample => "Recording sample",
                    _ => "Save / guard",
                };
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(T(name));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(sample.Count.ToString());
                ImGui.TableNextColumn(); ImGui.TextUnformatted(sample.Count == 0 ? "—" :
                    (sample.TotalTicks * 1000d / Stopwatch.Frequency / sample.Count).ToString("F3"));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(sample.Count == 0 ? "—" :
                    (sample.MaxTicks * 1000d / Stopwatch.Frequency).ToString("F3"));
            }
        }
        finally { ImGui.EndTable(); }
    }

    private void DrawLightingDiagnostics()
    {
        ImGui.TextUnformatted(T(settings.Enabled ? "Custom lighting: enabled" : "Custom lighting: off"));
        ImGui.TextUnformatted(F("Lighting writes: {0}", session.WriteCount));
        ImGui.TextUnformatted(F("Lighting releases: {0}", session.ReleaseCount));
        ImGui.TextUnformatted(F("Foreground reports: {0}", guard.ForegroundReports));
        ImGui.TextUnformatted(F("Background reports guarded: {0}", guard.BackgroundReports));
        ImGui.TextUnformatted(F("Inspection errors: {0}", guard.Errors));
        var error = runtime.LastError;
        if (error != null) ImGui.TextWrapped(F("Last output error: {0}", error));
    }

    private void DrawSfxDiagnostics(bool foreground, SfxAudioCapture? capture)
    {
        if (runtime.AudioError != null) ImGui.TextWrapped(F("SFX capture unavailable: {0}", runtime.AudioError));
        else if (capture?.Faulted == true) ImGui.TextWrapped(T("Sound capture stopped. Reload the plugin to retry."));
        else if (capture == null) ImGui.TextUnformatted(T(!runtime.AudioStarted ? "SFX capture: not started" : "Preparing SFX capture…"));
        else if (!capture.LayoutSupported) ImGui.TextWrapped(T("Waiting for a supported game audio mixer. SFX lighting is paused."));
        else if (settings.AudioSource == SfxSource.PlayerOnly && !capture.PlayerFilterSupported)
            ImGui.TextWrapped(T("Your sounds cannot be identified on this game version. Choose All combat sounds for lighting."));
        else ImGui.TextUnformatted(T(!foreground || !settings.Enabled || !LightingEffects.IsSfx(settings.Effect)
            ? "SFX lighting: paused" : "SFX lighting: ready"));
        ImGui.TextUnformatted(F("SFX RMS: {0:F4}; light level: {1:P0}", runtime.SfxRms, runtime.SfxLevel));
        if (capture == null) return;
        ImGui.TextUnformatted(F("SFX blocks: {0}; other voices skipped: {1}", capture.MatchedBlocks, capture.RejectedVoices));
        ImGui.TextUnformatted(F("SFX VFX: yours {0}; other {1}; unknown {2}", capture.OwnAnimations, capture.OtherAnimations, capture.UnknownAnimations));
        ImGui.TextUnformatted(F("Your sound reservations: {0}", capture.OwnedReservations));
        ImGui.TextUnformatted(F("Your playback links: {0}", capture.PlaybackLinks));
        if (capture.PlayerFilterError != null) ImGui.TextWrapped(F("Player-only setup: {0}", capture.PlayerFilterError));
    }

    private void DrawHapticDiagnostics(bool foreground, SfxAudioCapture? capture)
    {
        var snapshot = hapticSettings;
        var enabled = snapshot.Enabled;
        ImGui.TextUnformatted(T(configuration.HapticEnabled ? "Haptics: enabled" : "Haptics: off — your choices are kept"));
        if (!runtime.UsesRegularRumble && capture?.ConfirmationError != null)
            ImGui.TextWrapped(F("Enable confirmation unavailable: {0}", capture.ConfirmationError));
        var output = capture?.Haptics;
        if (runtime.RumbleError != null) ImGui.TextWrapped(F("Regular rumble unavailable: {0}", runtime.RumbleError));
        else if (runtime.BluetoothOutput)
        {
            ImGui.TextUnformatted(T(runtime.RumbleSlot >= 0 ? "Bluetooth rumble: ready" : "Bluetooth rumble: waiting for a DualSense"));
            ImGui.TextUnformatted(F("Bluetooth rumble mode: {0}", T(session.BluetoothImprovedRumble ? "Improved" : "Legacy")));
            var command = session.BluetoothMotorCommand;
            ImGui.TextUnformatted(F("Bluetooth motor command: left {0}/255; right {1}/255", command.Left, command.Right));
            var test = session.BluetoothStrengthTest;
            if (test.Writes > 0) ImGui.TextUnformatted(F("Strength test writes: {0}; motor range {1}–{2}/255", test.Writes, test.Minimum, test.Maximum));
            if (test.Writes > 0)
            {
                var pulses = session.BluetoothTestCommands;
                ImGui.TextUnformatted(F("Test pulse 1: left {0}/255; right {1}/255", pulses.FirstLeft, pulses.FirstRight));
                ImGui.TextUnformatted(F("Test pulse 2: left {0}/255; right {1}/255", pulses.SecondLeft, pulses.SecondRight));
            }
        }
        else if (runtime.UsesRegularRumble)
            ImGui.TextUnformatted(runtime.RumbleSlot >= 0 ? F("Regular rumble: controller {0}", runtime.RumbleSlot + 1) : T("Regular rumble: waiting for an XInput controller"));
        else ImGui.TextUnformatted(T("Vibration output: native DualSense haptics"));
        if (!enabled) ImGui.TextUnformatted(T("Player SFX: off"));
        else if (runtime.AudioError != null) ImGui.TextWrapped(F("Haptics unavailable: {0}", runtime.AudioError));
        else if (capture == null) ImGui.TextUnformatted(T("Preparing character sound detection…"));
        else if (capture.Faulted || output?.Faulted == true) ImGui.TextWrapped(T("Combat feedback stopped. Reload the plugin to retry."));
        else if (!capture.PlayerFilterSupported) ImGui.TextWrapped(T("Your sounds cannot be identified on this game version. Added feedback is paused."));
        else if (!runtime.UsesRegularRumble && capture.HapticsError != null) ImGui.TextWrapped(F("Native haptics unavailable: {0}", capture.HapticsError));
        else if (!capture.LayoutSupported) ImGui.TextWrapped(T("Waiting for a supported game audio mixer."));
        else if (!foreground) ImGui.TextUnformatted(T("Player SFX: paused in background"));
        else if (runtime.UsesRegularRumble && (runtime.RumbleSlot < 0 || runtime.RumbleError != null))
            ImGui.TextUnformatted(T("Player SFX: waiting for a rumble controller"));
        else if (snapshot.Strength <= 0) ImGui.TextUnformatted(T("Player SFX strength: 0 — native effects only"));
        else ImGui.TextUnformatted(T("Player SFX: ready — use a skill"));
        if (!snapshot.FootstepAmplifier) ImGui.TextUnformatted(T("Native footstep amplifier: off"));
        else if (!HapticOutputSelection.SupportsFootstepAmplification(snapshot.OutputMode, session.HasUsb, session.HasBluetooth))
            ImGui.TextWrapped(T("Connect a DualSense or DualSense Edge by USB and select Automatic or DualSense haptics."));
        else if (runtime.AudioError != null) ImGui.TextWrapped(F("Haptics unavailable: {0}", runtime.AudioError));
        else if (capture == null) ImGui.TextUnformatted(T("Preparing character sound detection…"));
        else if (!runtime.UsesRegularRumble && capture.HapticsError != null) ImGui.TextWrapped(F("Native haptics unavailable: {0}", capture.HapticsError));
        else if (capture.Faulted || output?.Faulted == true) ImGui.TextWrapped(T("Combat feedback stopped. Reload the plugin to retry."));
        else if (!capture.LayoutSupported) ImGui.TextWrapped(T("Waiting for a supported game audio mixer."));
        else if (!foreground) ImGui.TextUnformatted(T("Native footstep amplifier: paused in background"));
        else if (output?.FootstepContextReady != true) ImGui.TextUnformatted(T("Native footstep amplifier: waiting for an unmounted character"));
        else if (snapshot.FootstepMultiplier <= 1) ImGui.TextUnformatted(T("Footstep vibration: 100% — native strength"));
        else ImGui.TextUnformatted(T("Native footstep amplifier: ready — walk or run"));
        if ((snapshot.Spatial || snapshot.DistanceFalloff) && snapshot.CombatEnabled)
        {
            if (capture?.SpatialError != null) ImGui.TextWrapped(F("Spatial haptics unavailable: {0}. ", capture.SpatialError) +
                T(configuration.HapticDistanceFalloff ? "Added feedback paused while distance fade is on." : "Using original combat haptics."));
            else if ((enabled || snapshot.EnemiesEnabled || snapshot.HitReactions) && output?.ListenerReady != true) ImGui.TextDisabled(T("Waiting for your character and an active gameplay camera."));
            var last = foreground && (enabled || snapshot.EnemiesEnabled || snapshot.HitReactions) ? output?.LastSpatial ?? default : default;
            if (!runtime.UsesRegularRumble || runtime.BluetoothOutput)
            {
                ImGui.ProgressBar(last.Left, new Vector2(0, 0), T("Left grip gain"));
                ImGui.ProgressBar(last.Right, new Vector2(0, 0), T("Right grip gain"));
            }
            var reference = T(snapshot.DistanceOrigin == HapticDistanceOrigin.Camera ? "camera" : "character");
            if (last.Valid) ImGui.TextUnformatted(F("Last sound: {0:F1} yalms from {1}; balance {2:+0.00;-0.00;0.00}", last.Distance, reference, last.Pan));
            if (snapshot.DistanceFalloff)
            {
                var pose = output?.Listener;
                if (snapshot.AutomaticDistance && snapshot.DistanceOrigin == HapticDistanceOrigin.Camera && pose == null)
                    ImGui.TextUnformatted(T("Distance fade: waiting for camera position"));
                else
                {
                    var range = HapticSpatializer.DistanceRange(snapshot, pose);
                    var distanceReference = T(snapshot.DistanceOrigin == HapticDistanceOrigin.Camera ? "camera" : "character");
                    ImGui.TextUnformatted(F("Distance fade ({0}): full at {1:F1}; zero at {2:F1} yalms", distanceReference, range.Near, range.Far));
                }
            }
        }
        ImGui.TextUnformatted(F("Combat blocks added: {0}", output?.MixedBlocks ?? 0));
        ImGui.TextUnformatted(F("Player hit-reaction blocks: {0}", output?.HitReactionBlocks ?? 0));
        if (snapshot.ScreenEffects)
        {
            if (capture?.ScreenEffectsError != null) ImGui.TextWrapped(F("Screen effect detection unavailable: {0}", capture.ScreenEffectsError));
            if (!runtime.UsesRegularRumble && capture?.ConfirmationError != null)
                ImGui.TextWrapped(F("Screen effect output unavailable: {0}", capture.ConfirmationError));
            ImGui.TextUnformatted(F("Screen effect level: {0:P0}", output?.ScreenEffectLevel(Stopwatch.GetTimestamp()) ?? 0));
            ImGui.TextUnformatted(F("Screen effect blocks: {0}; suppressed: {1}", output?.ScreenBlocks ?? 0, output?.ScreenSuppressedBlocks ?? 0));
        }
        ImGui.TextUnformatted(F("Enemy blocks: bosses {0}; regular enemies {1}", output?.BossBlocks ?? 0, output?.RegularEnemyBlocks ?? 0));
        ImGui.TextUnformatted(F("Boss SFX rule: {0}; remembered regular enemies: {1}",
            T(capture?.EnemyEncounter.BossSfx == true ? "active" : "inactive"), runtime.BossCount));
        if (capture?.EnemySkillError != null) ImGui.TextWrapped(F("Enemy skill detection unavailable: {0}", capture.EnemySkillError));
        if (capture?.ArenaSoundError != null) ImGui.TextWrapped(F("Ground-effect audio unavailable: {0}", capture.ArenaSoundError));
        if (snapshot.BossEnabled) ImGui.TextUnformatted(F("Active ground-effect sources: {0}", capture?.ArenaSources.Length ?? 0));
        ImGui.TextUnformatted(F("Enemy actions: skills {0}; basic attacks {1}; unknown {2}",
            capture?.EnemySkillActions ?? 0, capture?.EnemyAutoAttacks ?? 0, capture?.UnknownEnemyActions ?? 0));
        ImGui.TextUnformatted(T(!snapshot.EnemiesEnabled ? "Enemy haptics: off" : capture?.EnemySkillError != null && capture?.EnemyEncounter.BossSfx != true
            ? "Enemy haptics: detection unavailable" : runtime.IsPvP
            ? "Enemy haptics: paused in PvP" : !foreground ? "Enemy haptics: paused in background" :
            output?.EnemiesActive == true ? "Enemy haptics: active" : "Enemy haptics: waiting for sound detection"));
        ImGui.TextUnformatted(F("Footstep blocks amplified: {0}", output?.FootstepBlocks ?? 0));
        ImGui.TextUnformatted(F("Native vibration blocks preserved: {0}", output?.PreservedNativeBlocks ?? 0));
        ImGui.TextUnformatted(F("Spatial blocks: {0}; original-route fallbacks: {1}", output?.SpatialBlocks ?? 0, output?.UnpositionedBlocks ?? 0));
        ImGui.TextUnformatted(F("Distance cutoff: {0}; missing position/listener: {1}", output?.CutoffBlocks ?? 0, output?.SuppressedBlocks ?? 0));
        ImGui.TextUnformatted(F("Player-origin blocks biased: {0}; impact cue: {1}", output?.BiasedBlocks ?? 0, T(output?.ImpactCue.HasValue == true ? "active" : "none")));
        if (!runtime.UsesRegularRumble)
            ImGui.TextUnformatted(T(output?.Available == true ? "Game vibration mixer detected" : "Waiting for the first eligible skill sound"));
    }

    internal bool RequestTraceToggle()
    {
        if (!DiagnosticsAvailable) return false;
        selectDiagnostics = true;
        if (!recording.Active && !configuration.CanRecordDiagnostics())
        {
            if (!configuration.HasCompletedSetup) showGeneralInfo = true;
            else showRecordingNotice = true;
            Open();
            return true;
        }
        recording.RequestToggle();
        return true;
    }

    private void PersistRecordingConsent()
    {
        var previous = configuration.HasAcknowledgedRecording;
        configuration.HasAcknowledgedRecording = true;
        try { Save(); }
        catch { configuration.HasAcknowledgedRecording = previous; throw; }
    }

    private void DrawRecordingLocations(bool alwaysVisible = false)
    {
        if (!alwaysVisible)
        {
            if (!ImGui.CollapsingHeader(Label("File locations and cleanup"))) return;
            ImGui.TextWrapped(T("Stop recording before deleting the latest files or their archives."));
            ImGui.Spacing();
        }
        DrawInfoItem("SFX recordings", F("Latest file: {0}", recording.Path));
        ImGui.TextWrapped(F("Archive folder: {0}", recording.ArchiveDirectory));
        ImGui.Spacing();
        DrawInfoItem("Camera-timing recordings", F("Latest file: {0}", recording.CameraPath));
        ImGui.TextWrapped(F("Archive folder: {0}", TraceArchive.ArchiveDirectory(recording.CameraPath)));
        ImGui.Spacing();
    }

    private void DrawRecordingStorage()
    {
        recording.RefreshTraceSize();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(recording.DiskBytes is long bytes
            ? F("Saved recordings on disk: {0:F2} MiB", bytes / (1024d * 1024d))
            : T("Saved recordings on disk: unavailable"));
        ImGui.SameLine();
        var busy = TraceFilesBusy;
        ImGui.BeginDisabled(busy || recording.DiskBytes == 0);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3, 2) * (ImGui.GetFontSize() / 17f));
        try
        {
            if (ImGuiComponents.IconButton("DeleteRecordings", FontAwesomeIcon.Trash))
            {
                noticeError = null;
                OpenDialog(DialogKind.DeleteRecordings);
            }
        }
        finally { ImGui.PopStyleVar(); ImGui.EndDisabled(); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            DrawTooltip(busy ? "Stop recording and wait for saves to finish."
                : recording.DiskBytes == 0 ? "No saved recordings." : "Delete saved recordings");
        DrawRecordingLocations(alwaysVisible: true);
    }

    private void DrawDeleteRecordingsConfirmation()
    {
        ImGui.TextUnformatted(T("Delete saved recordings?"));
        Divider();
        ImGui.Spacing();
        ImGui.TextWrapped(T("Delete all saved SFX and camera-timing recordings, including archives? This cannot be undone."));
        var actions = ConsentActionLayout("Cancel", "Delete recordings");
        DrawConsentFooter(actions.Size, actions.Stack);
        if (ImGui.Button(Label("Cancel"), actions.Size)) { CloseDialog(); return; }
        ImGui.SetItemDefaultFocus();
        if (!actions.Stack) ImGui.SameLine();
        ImGui.BeginDisabled(TraceFilesBusy);
        try
        {
            if (ImGui.Button(Label("Delete recordings"), actions.Size))
            {
                recording.RequestDelete();
                CloseDialog();
            }
        }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawRecordingNotice()
    {
        DrawConsentHeading("Before recording");
        var actions = ConsentActionLayout("Cancel", "Start recording");
        var body = BeginConsentBody(30, actions.Height);
        try
        {
            if (body)
            {
                DrawInfoItem("What is saved", "Diagnostics saves game-state metadata, not audio. It can include timestamps, sound-resource paths, positions, entity IDs, memory addresses and small native-memory snapshots.");
                DrawInfoItem("Sharing", "Files stay on your computer. Nothing is uploaded automatically. Review recordings before sharing them, especially on public GitHub issues.");
                DrawInfoItem("Storage", "SFX records one-minute segments until you stop it. Each location normally keeps the latest 20 archives plus a latest-file copy. Recordings can become large; stop when finished.");
                DrawRecordingLocations();
            }
        }
        finally { ImGui.EndChild(); }
        DrawConsentFooter(actions.Size, actions.Stack);
        if (ImGui.Button(Label("Cancel"), actions.Size)) CloseDialog();
        ImGui.SetItemDefaultFocus();
        if (!actions.Stack) ImGui.SameLine();
        if (ImGui.Button(Label("Start recording"), actions.Size))
        {
            try
            {
                PersistRecordingConsent();
                noticeError = null;
                RequestTraceToggle();
                CloseDialog();
            }
            catch (Exception error)
            {
                noticeError = error.Message;
                reportError(error);
            }
        }
    }
}
