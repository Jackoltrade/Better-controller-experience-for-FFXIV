// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using DualSenseLighting.Ui;
using DualSenseLighting.Core;

namespace DualSenseLighting;

public sealed partial class Plugin
{
    private string? noticeError;

    private void DrawConsentHeading(string title)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var languageWidth = ImGui.GetFontSize() * 8 + ImGui.GetStyle().ItemInnerSpacing.X
            + ImGui.CalcTextSize("Language / 语言 / 言語").X;
        var languageX = ImGui.GetCursorPosX() + available - languageWidth;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(T(title));
        if (ImGui.CalcTextSize(T(title)).X + languageWidth + ImGui.GetStyle().ItemSpacing.X <= available)
            ImGui.SameLine(languageX);
        DrawLanguageSelector();
        Divider();
        ImGui.Spacing();
    }

    private static bool BeginConsentBody(float lines, float footerHeight)
    {
        var style = ImGui.GetStyle();
        var remaining = ImGui.GetMainViewport().WorkSize.Y * DialogWindow.ViewportHeightFraction - ImGui.GetCursorPosY()
            - style.WindowPadding.Y - style.WindowBorderSize * 2 - style.ItemSpacing.Y - footerHeight;
        var height = Math.Max(1, Math.Min(ImGui.GetFontSize() * lines, remaining));
        return ImGui.BeginChild("##ConsentBody", new Vector2(0, height), false, ImGuiWindowFlags.NoSavedSettings);
    }

    private (Vector2 Size, bool Stack, float Height) ConsentActionLayout(string? secondary, string primary, string? alternate = null)
    {
        var style = ImGui.GetStyle();
        var available = ImGui.GetContentRegionAvail().X;
        var width = Math.Max(ImGui.CalcTextSize(T(secondary ?? primary)).X,
            Math.Max(ImGui.CalcTextSize(T(primary)).X, ImGui.CalcTextSize(T(alternate ?? primary)).X))
            + style.FramePadding.X * 2 + ImGui.GetFontSize();
        var stack = secondary != null && width * 2 + style.ItemSpacing.X > available;
        var rows = stack ? 2 : 1;
        var height = rows * ImGui.GetFrameHeight() + (rows + 3) * style.ItemSpacing.Y + 1;
        if (noticeError != null)
            height += ImGui.CalcTextSize(T("Could not save your choice. Hover for details."), false, available).Y + style.ItemSpacing.Y;
        return (new Vector2(secondary == null || stack ? available : width, 0), stack, height);
    }

    private void DrawConsentFooter(Vector2 size, bool stack, bool single = false)
    {
        Divider();
        ImGui.Spacing();
        if (noticeError != null)
        {
            ImGui.TextWrapped(T("Could not save your choice. Hover for details."));
            if (ImGui.IsItemHovered()) DrawTooltip(F("Could not save your choice: {0}", noticeError));
        }
        if (!single && !stack)
        {
            var total = size.X * 2 + ImGui.GetStyle().ItemSpacing.X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - total));
        }
    }

    private void DrawGeneralInfo()
    {
        if (!configuration.HasSeenGeneralInfo)
        {
            configuration.HasSeenGeneralInfo = true;
            saveAt = clock.Elapsed.TotalSeconds + 0.5;
        }
        DrawConsentHeading("Before you start");
        var actions = configuration.HasCompletedSetup
            ? ConsentActionLayout(null, "Got it")
            : ConsentActionLayout("Later", "Enable selected effects", "Keep effects off");
        var body = BeginConsentBody(32, actions.Height);
        try
        {
            if (body)
            {
                if (!configuration.HasCompletedSetup) DrawFirstRunChoices();
                else ImGui.TextWrapped(T("Your changes are saved automatically. You can reopen this guide with Info in the top-left corner."));
                ImGui.Spacing();
                DrawInfoItem("Good to know", "Detailed DualSense haptics and controller-speaker effects need USB. Enable controller vibration in FFXIV.");
                ImGui.TextWrapped(T("Effects use native game hooks and run locally. No microphone recording, telemetry or automatic uploads."));
                ImGui.Spacing();
                if (ImGui.CollapsingHeader(Label("Compatibility and game settings")))
                {
                    DrawInfoItem("Supported controllers", "DualSense and DualSense Edge support lighting over USB or Bluetooth. Xbox-compatible XInput controllers support regular vibration.");
                    DrawInfoItem("Connection", "Detailed DualSense haptics, native footstep amplification and controller-speaker effects require USB. Bluetooth supports lighting and basic rumble.");
                    DrawInfoItem("Game settings", "Enable controller vibration in FFXIV to use haptics. Set up controller-speaker effects in the game as usual.");
                    DrawInfoItem("Switching apps", "Custom lights turn off and added vibration pauses when FFXIV is in the background. They resume when you return.");
                }
                if (ImGui.CollapsingHeader(Label("How it works and privacy")))
                {
                    DrawInfoItem("How it works", "The plugin reads sound, camera and encounter data inside FFXIV and hooks native functions to add controller effects. Game updates and other plugins can affect compatibility.");
                    DrawInfoItem("Privacy", "Game audio is processed locally. No extra controller drivers or services are installed. There is no microphone recording, telemetry or automatic upload. Diagnostic recording is optional.");
                }
            }
        }
        finally { ImGui.EndChild(); }
        if (!configuration.HasCompletedSetup) DrawFirstRunActions(actions.Size, actions.Stack);
        else
        {
            DrawConsentFooter(actions.Size, actions.Stack, single: true);
            if (ImGui.Button(Label("Got it"), actions.Size)) CloseDialog();
            ImGui.SetItemDefaultFocus();
        }
    }

    private void DrawInfoItem(string heading, string description)
    {
        ImGui.TextUnformatted(T(heading));
        ImGui.TextWrapped(T(description));
        ImGui.Spacing();
    }

    private void DrawFirstRunChoices()
    {
        ImGui.TextUnformatted(T("Choose your effects"));
        ImGui.TextWrapped(T("Nothing changes until you save. You can adjust these choices later."));
        ImGui.Spacing();
        ImGui.Checkbox(Label("Enable custom lighting"), ref setupLighting);
        DrawChoiceDescription("RGB lightbar and bottom LEDs. DualSense only.");
        ImGui.Checkbox(Label("Enable haptics"), ref setupHaptics);
        DrawChoiceDescription("Add vibration for combat and screen effects.");
    }

    private void DrawChoiceDescription(string description)
    {
        var indent = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
        ImGui.Indent(indent);
        ImGui.TextWrapped(T(description));
        ImGui.Unindent(indent);
        ImGui.Spacing();
    }

    private void DrawFirstRunActions(Vector2 size, bool stack)
    {
        var primary = setupLighting || setupHaptics ? "Enable selected effects" : "Keep effects off";
        DrawConsentFooter(size, stack);
        if (ImGui.Button(Label("Later"), size)) CloseDialog();
        ImGui.SetItemDefaultFocus();
        if (!stack) ImGui.SameLine();
        if (ImGui.Button(Label(primary), size))
        {
            try
            {
                var previous = (configuration.Enabled, configuration.HapticEnabled, configuration.HasSeenGeneralInfo);
                configuration.CompleteSetup(setupLighting, setupHaptics);
                try { Save(); }
                catch
                {
                    configuration.Enabled = previous.Enabled;
                    configuration.HapticEnabled = previous.HapticEnabled;
                    configuration.HasSeenGeneralInfo = previous.HasSeenGeneralInfo;
                    configuration.HasCompletedSetup = false;
                    throw;
                }
                // Persist the explicit choice before activating output or hooks.
                Changed();
                saveAt = double.PositiveInfinity;
                noticeError = null;
                CloseDialog();
            }
            catch (Exception error)
            {
                if (!setupReady)
                {
                    configuration.HasCompletedSetup = false;
                    saveAt = clock.Elapsed.TotalSeconds + 0.5;
                }
                noticeError = error.Message;
                ReportError(error);
            }
        }
    }

    private void DrawRecordingLocations(bool alwaysVisible = false)
    {
        if (!alwaysVisible)
        {
            if (!ImGui.CollapsingHeader(Label("File locations and cleanup"))) return;
            ImGui.TextWrapped(T("Stop recording before deleting the latest files or their archives."));
            ImGui.Spacing();
        }
        DrawInfoItem("SFX recordings", F("Latest file: {0}", TracePath));
        ImGui.TextWrapped(F("Archive folder: {0}", TraceDirectory));
        ImGui.Spacing();
        DrawInfoItem("Camera-timing recordings", F("Latest file: {0}", TraceArchive.ScreenTimingPath));
        ImGui.TextWrapped(F("Archive folder: {0}", TraceArchive.ArchiveDirectory(TraceArchive.ScreenTimingPath)));
        ImGui.Spacing();
    }

    private bool TraceFilesBusy => sfxTrace != null || traceSave != null ||
        Volatile.Read(ref traceDeleteRequested) != 0 || Volatile.Read(ref traceToggleRequested) != 0;

    private void DeleteRecordings()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        if (sfxTrace != null || traceSave != null)
        { traceStatus = "Stop recording and wait for saves to finish."; return; }
        // The camera recorder owns a separate save task. Stop it before taking
        // the task snapshot, then reserve our save queue so a new trace cannot
        // start while either writer is finishing or files are being removed.
        var capture = Volatile.Read(ref audioCapture);
        capture?.SetTracing(false);
        var cameraSave = capture?.TraceSaveCompletion ?? Task.CompletedTask;
        var sfxPath = TracePath;
        var cameraPath = TraceArchive.ScreenTimingPath;
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

    private void DrawRecordingStorage()
    {
        RefreshTraceSize();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(traceDiskBytes is long bytes
            ? F("Saved recordings on disk: {0:F2} MiB", bytes / (1024d * 1024d))
            : T("Saved recordings on disk: unavailable"));
        ImGui.SameLine();
        var busy = TraceFilesBusy;
        ImGui.BeginDisabled(busy || traceDiskBytes == 0);
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
                : traceDiskBytes == 0 ? "No saved recordings." : "Delete saved recordings");
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
                Interlocked.Exchange(ref traceDeleteRequested, 1);
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
            configuration.HasAcknowledgedRecording = true;
            try
            {
                Save();
                noticeError = null;
                RequestTraceToggle();
                CloseDialog();
            }
            catch (Exception error)
            {
                configuration.HasAcknowledgedRecording = false;
                noticeError = error.Message;
                ReportError(error);
            }
        }
    }
}
