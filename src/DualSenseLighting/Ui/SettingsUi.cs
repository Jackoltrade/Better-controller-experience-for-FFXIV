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

internal readonly record struct UiRuntimeState(SfxAudioCapture? Capture, GameLimitBreak? LimitBreak,
    string? AudioError, string? LimitBreakError, string? RumbleError, string? LastError,
    float SfxLevel, float SfxRms, bool BluetoothOutput, bool UsesRegularRumble, int RumbleSlot, int BossCount, LimitBreakState LimitBreakState, bool IsPvP, bool MeasureUpdates, long UpdateSamples, long UpdateTotalTicks, long UpdateMaxTicks, bool AudioStarted);

internal sealed class SettingsUi : IDisposable
{
    private static readonly string PluginVersion = typeof(SettingsUi).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    private readonly Configuration configuration;
    private readonly UiText uiText;
    private readonly IFontHandle uiFont;
    private readonly WindowSystem windowSystem = new("DualSenseLighting");
    private readonly SettingsWindow settingsWindow;
    private readonly DialogWindow dialogWindow;
    private readonly ControllerPreview preview;
    private readonly LightingSession session;
    private readonly GameOutputGuard guard;
    private readonly DiagnosticRecordingCoordinator recording;
    private readonly ConfigurationSaveScheduler saves;
    private readonly Stopwatch clock;
    private readonly Func<LightingSettings> getLighting;
    private readonly Func<HapticSettings> getHaptics;
    private readonly Func<UiRuntimeState> getRuntime;
    private readonly Action Changed, publishSavedSetup, Save, refreshCommandHelp;
    private readonly Action<int> requestRumbleTest;
    private readonly Action<bool> measureUpdates;
    private readonly Action<Exception> reportError;
    private LightingSettings settings => getLighting();
    private HapticSettings hapticSettings => getHaptics();
    private UiRuntimeState runtime;
    private int disposed;
    private DialogKind dialogKind;
    private enum DialogKind { Info, Recording, DeleteRecordings, Reset }
    private enum ResetTarget { Lighting, Haptics }
    private ResetTarget? resetTarget;
    private bool showGeneralInfo, setupLighting, setupHaptics, showRecordingNotice, selectDiagnostics;
    internal bool IsOpen => settingsWindow.IsOpen;
    internal void ShowInfo() => showGeneralInfo = true;
    internal SettingsUi(Configuration configuration, UiText uiText, IDalamudPluginInterface pluginInterface,
        ITextureProvider textures, LightingSession session, GameOutputGuard guard,
        DiagnosticRecordingCoordinator recording, ConfigurationSaveScheduler saves, Stopwatch clock,
        Func<LightingSettings> lighting, Func<HapticSettings> haptics, Func<UiRuntimeState> runtime,
        Action changed, Action publishSavedSetup, Action save, Action<int> rumbleTest, Action refreshCommandHelp, Action<Exception> reportError, Action<bool> measureUpdates)
    {
        this.configuration = configuration; this.uiText = uiText; this.session = session; this.guard = guard;
        this.recording = recording; this.saves = saves; this.clock = clock; getLighting = lighting; getHaptics = haptics;
        getRuntime = runtime; Changed = changed; this.publishSavedSetup = publishSavedSetup; Save = save; requestRumbleTest = rumbleTest;
        this.refreshCommandHelp = refreshCommandHelp;
        this.reportError = reportError; this.measureUpdates = measureUpdates;
        uiText.Language = Enum.IsDefined(configuration.Language) ? configuration.Language : UiLanguage.English;
        setupLighting = configuration.Enabled; setupHaptics = configuration.HapticEnabled;
        showGeneralInfo = !configuration.HasCompletedSetup;
        dialogWindow = new DialogWindow(DrawDialog);
        settingsWindow = new SettingsWindow(DrawSettings) { IsOpen = showGeneralInfo, Closed = () => dialogWindow.IsOpen = false };
        preview = new ControllerPreview(textures, uiText);
        IFontHandle? acquiredFont = null;
        try
        {
            uiFont = acquiredFont = pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
                e.OnPreBuild(tk => tk.AddDalamudDefaultFont(-1, UiText.GlyphRanges)));
            windowSystem.AddWindow(settingsWindow); windowSystem.AddWindow(dialogWindow);
        }
        catch
        {
            try { ResourceCleanup.Run(() => windowSystem.RemoveAllWindows(), () => acquiredFont?.Dispose()); } catch { }
            throw;
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        ResourceCleanup.Run(() => windowSystem.RemoveAllWindows(), () => uiFont.Dispose());
    }

    private void DrawColor(bool secondary, string label)
    {
        var color = (secondary
            ? new Vector3(configuration.SecondaryRed, configuration.SecondaryGreen, configuration.SecondaryBlue)
            : new Vector3(configuration.Red, configuration.Green, configuration.Blue)) / 255;
        if (!ImGui.ColorEdit3(Label(label), ref color)) return;
        var red = (byte)Math.Clamp((int)Math.Round(color.X * 255), 0, 255);
        var green = (byte)Math.Clamp((int)Math.Round(color.Y * 255), 0, 255);
        var blue = (byte)Math.Clamp((int)Math.Round(color.Z * 255), 0, 255);
        if (secondary)
        { configuration.SecondaryRed = red; configuration.SecondaryGreen = green; configuration.SecondaryBlue = blue; }
        else
        { configuration.Red = red; configuration.Green = green; configuration.Blue = blue; }
        Changed();
    }

    internal void Draw()
    {
        // Include the title bar and PreDraw constraints in the language font.
        runtime = getRuntime();
        using var font = uiFont.Push();
        windowSystem.Draw();
    }

    private void DrawSettings()
    {
        ImGui.PushTextWrapPos(0);
        ImGui.PushItemWidth(ImGui.GetFontSize() * 14);
        try
        {
            var showInfo = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.InfoCircle, Label("Info"));
            if (ImGui.IsItemHovered()) DrawTooltip("Setup and controller compatibility.");
            ImGui.SameLine();
            DrawLanguageSelector();
            if (showInfo || showGeneralInfo || !configuration.HasSeenGeneralInfo)
            {
                OpenDialog(DialogKind.Info);
                showGeneralInfo = false;
            }
            if (showRecordingNotice)
            {
                OpenDialog(DialogKind.Recording);
                showRecordingNotice = false;
            }
            if (!configuration.HasCompletedSetup)
                ImGui.TextWrapped(T("Controller effects are paused. Open Info to choose what to enable."));
            var foreground = Native.IsCurrentProcessForeground();
            ResetTarget? activeSettings = null;
            if (ImGui.BeginTabBar("DualSenseSettingsTabs"))
            {
                try
                {
                    if (ImGui.BeginTabItem(Label("Lighting")))
                    {
                        activeSettings = ResetTarget.Lighting;
                        ImGui.BeginDisabled(!configuration.HasCompletedSetup);
                        try { DrawLighting(foreground); }
                        finally { ImGui.EndDisabled(); ImGui.EndTabItem(); }
                    }
                    if (ImGui.BeginTabItem(Label("Haptics")))
                    {
                        activeSettings = ResetTarget.Haptics;
                        ImGui.BeginDisabled(!configuration.HasCompletedSetup);
                        try { DrawHaptics(); }
                        finally { ImGui.EndDisabled(); ImGui.EndTabItem(); }
                    }
                    if (ImGui.BeginTabItem(Label("Diagnostics"), selectDiagnostics ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
                    {
                        selectDiagnostics = false;
                        try { DrawDiagnostics(foreground); }
                        finally { ImGui.EndTabItem(); }
                    }
                    if (ImGui.BeginTabItem(Label("About")))
                    {
                        try { DrawAbout(); }
                        finally { ImGui.EndTabItem(); }
                    }
                }
                finally { ImGui.EndTabBar(); }
            }
            ImGui.Spacing();
            Divider();
            if (ImGui.Button(Label("Close"))) settingsWindow.IsOpen = false;
            Help("Changes save automatically.");
            if (saves.Error != null) ImGui.TextWrapped(F("Could not save settings; retrying automatically: {0}", saves.Error));
            if (activeSettings.HasValue)
            {
                ImGui.SameLine();
                ImGui.BeginDisabled(!configuration.HasCompletedSetup);
                if (ImGui.Button(Label("Reset to defaults")))
                {
                    resetTarget = activeSettings.Value;
                    OpenDialog(DialogKind.Reset);
                }
                ImGui.EndDisabled();
                Help("Restore defaults for this tab.");
            }
        }
        finally { ImGui.PopItemWidth(); ImGui.PopTextWrapPos(); }
    }

    private void OpenDialog(DialogKind kind)
    {
        dialogKind = kind;
        dialogWindow.Title = DialogTitle();
        dialogWindow.Width = kind == DialogKind.Reset ? 450 : 560;
        dialogWindow.IsOpen = true;
        dialogWindow.BringToFront();
    }

    private void CloseDialog() => dialogWindow.IsOpen = false;

    private string DialogTitle() => T(dialogKind switch
    {
        DialogKind.Info => "Before you start",
        DialogKind.Recording => "Before recording",
        DialogKind.DeleteRecordings => "Delete saved recordings?",
        _ => resetTarget == ResetTarget.Lighting ? "Reset lighting?" : "Reset haptics?",
    });

    private void DrawDialog()
    {
        dialogWindow.Title = DialogTitle();
        ImGui.PushTextWrapPos(0);
        try
        {
            switch (dialogKind)
            {
                case DialogKind.Info: DrawGeneralInfo(); break;
                case DialogKind.Recording: DrawRecordingNotice(); break;
                case DialogKind.DeleteRecordings: DrawDeleteRecordingsConfirmation(); break;
                case DialogKind.Reset: DrawResetConfirmation(); break;
            }
        }
        finally { ImGui.PopTextWrapPos(); }
    }

    private void DrawAbout()
    {
        DrawAboutText(PluginInfo.Name);
        DrawAboutText(F("Version {0} · Public test build", PluginVersion), true);
        DrawAboutText(T("Make FFXIV more immersive with custom DualSense lighting and vibration."));

        Section("Project", true);
        DrawAboutText(F("Author: {0}", PluginInfo.Author));
        DrawAboutText(F("License: {0}", PluginInfo.License));
        DrawAboutText(T("Windows · DualSense or XInput controller · Dalamud API 15"));
        ImGui.Spacing();
        CenterAboutItem(ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.CodeBranch, T("View source on GitHub")));
        DrawAboutLink("View source on GitHub", FontAwesomeIcon.CodeBranch, PluginInfo.RepositoryUrl);

        Section("Support the project", true);
        DrawAboutText(T("Enjoying the plugin? Buy me a coffee."));
        ImGui.Spacing();
        CenterAboutRow("Support on Afdian", FontAwesomeIcon.Bolt, "Support on Ko-fi", FontAwesomeIcon.MugHot);
        DrawAboutLink("Support on Afdian", FontAwesomeIcon.Bolt, PluginInfo.AfdianUrl, available: PluginInfo.AfdianAvailable);
        ImGui.SameLine();
        DrawAboutLink("Support on Ko-fi", FontAwesomeIcon.MugHot, PluginInfo.KofiUrl, available: PluginInfo.KofiAvailable);
        ImGui.Spacing();
        DrawAboutText(T("This plugin is completely free to use."), true);

        Section("Feedback", true);
        DrawAboutText(T("Found a bug or have an idea? I'd love to hear from you."));
        ImGui.Spacing();
        CenterAboutRow("Send feedback by email", FontAwesomeIcon.Envelope, "Feedback on GitHub", FontAwesomeIcon.CommentDots);
        DrawAboutLink("Send feedback by email", FontAwesomeIcon.Envelope, PluginInfo.FeedbackUrl,
            "Opens a draft with the subject filled in.");
        ImGui.SameLine();
        DrawAboutLink("Feedback on GitHub", FontAwesomeIcon.CommentDots, PluginInfo.GitHubFeedbackUrl);

        Section("Credits", true);
        DrawAboutText(T("Built with Dalamud. Controller support draws on DS4Windows; audio bindings draw on FFXIVClientStructs. Their licenses and credits are included with the plugin."));
    }

    private static void CenterAboutItem(float width)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2));
    }

    private void CenterAboutRow(string first, FontAwesomeIcon firstIcon, string second, FontAwesomeIcon secondIcon)
    {
        CenterAboutItem(ImGuiComponents.GetIconButtonWithTextWidth(firstIcon, T(first)) +
            ImGui.GetStyle().ItemSpacing.X + ImGuiComponents.GetIconButtonWithTextWidth(secondIcon, T(second)));
    }

    private static void DrawAboutText(string text, bool disabled = false)
    {
        // Reuse ImGui's UTF-8 word wrapping, centering each rendered line so long
        // descriptions align correctly in English, Chinese and Japanese alike.
        Span<byte> utf8 = stackalloc byte[Encoding.UTF8.GetByteCount(text)];
        Encoding.UTF8.GetBytes(text, utf8);
        ReadOnlySpan<byte> remaining = utf8;
        var startX = ImGui.GetCursorPosX();
        var width = ImGui.GetContentRegionAvail().X;
        var font = ImGui.GetFont();
        var scale = ImGui.GetFontSize() / font.FontSize;
        ImGui.PushTextWrapPos(-1);
        try
        {
            while (!remaining.IsEmpty)
            {
                var length = font.CalcWordWrapPositionA(scale, remaining, width);
                if (length <= 0) Rune.DecodeFromUtf8(remaining, out _, out length);
                var line = remaining[..length];
                ImGui.SetCursorPosX(startX + Math.Max(0, (width - ImGui.CalcTextSize(line).X) / 2));
                if (disabled) ImGui.TextDisabled(line);
                else ImGui.TextUnformatted(line);
                remaining = remaining[length..].TrimStart((byte)' ');
            }
        }
        finally { ImGui.PopTextWrapPos(); ImGui.SetCursorPosX(startX); }
    }

    private void DrawAboutLink(string label, FontAwesomeIcon icon, string? url, string? tooltip = null, bool available = true)
    {
        var padding = ImGui.GetStyle().FramePadding;
        padding.Y = Math.Max(padding.Y, ImGui.GetFontSize() * 0.25f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, padding);
        ImGui.BeginDisabled(!available);
        try
        {
            if (ImGuiComponents.IconButtonWithText(icon, Label(label))) Util.OpenLink(url!);
        }
        finally { ImGui.EndDisabled(); ImGui.PopStyleVar(); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (!available) DrawTooltip("This support link will be added soon.");
            else if (tooltip != null) DrawTooltip(tooltip);
        }
    }

    private void DrawResetConfirmation()
    {
        if (!resetTarget.HasValue) { CloseDialog(); return; }
        ImGui.TextWrapped(T(resetTarget == ResetTarget.Lighting
            ? "Restore the default lighting settings? Your haptics settings and language will stay the same."
            : "Restore the default haptics settings? Your lighting settings and language will stay the same."));
        ImGui.Spacing();
        if (ImGui.Button(Label("Cancel")))
        {
            CloseDialog();
            resetTarget = null;
        }
        ImGui.SetItemDefaultFocus();
        ImGui.SameLine();
        if (ImGui.Button(Label("Reset")))
        {
            if (resetTarget == ResetTarget.Lighting) configuration.ResetLightingToDefaults();
            else if (resetTarget == ResetTarget.Haptics) configuration.ResetHapticsToDefaults();
            Changed();
            Save();
            CloseDialog();
            resetTarget = null;
        }
    }

    private void Section(string heading, bool centered = false)
    {
        ImGui.Dummy(new Vector2(0, ImGui.GetFontSize()));
        if (centered) DrawAboutText(T(heading));
        else ImGui.TextUnformatted(T(heading));
        Divider();
        ImGui.Spacing();
    }

    private void Subsection(string heading)
    {
        ImGui.Dummy(new Vector2(0, ImGui.GetFontSize() * 0.75f));
        ImGui.TextUnformatted(T(heading));
        ImGui.Spacing();
    }

    private static unsafe void Divider()
    {
        var color = *ImGui.GetStyleColorVec4(ImGuiCol.Text);
        color.W = 0.28f;
        ImGui.PushStyleColor(ImGuiCol.Separator, color);
        try { ImGui.Separator(); }
        finally { ImGui.PopStyleColor(); }
    }

    private string T(string english) => uiText.Get(english);
    private string Label(string english) => uiText.Label(english);
    private string F(string english, params object[] values) => uiText.Format(english, values);

    private void DrawLanguageSelector()
    {
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
        var language = (int)uiText.Language;
        if (ImGui.Combo("Language / 语言 / 言語###Language", ref language, "English\0简体中文\0日本語\0"))
        {
            configuration.Language = uiText.Language = (UiLanguage)language;
            refreshCommandHelp();
            saves.MarkDirty(clock.Elapsed.TotalSeconds);
        }
        ImGui.Spacing();
    }

    private void Help(string text)
    {
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        ImGui.SameLine();
        ImGui.TextDisabled(T("(?)"));
        if (!hovered && !ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        DrawTooltip(text);
    }

    private void DrawTooltip(string text)
    {
        ImGui.BeginTooltip();
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26);
            try { ImGui.TextUnformatted(T(text)); }
            finally { ImGui.PopTextWrapPos(); }
        }
        finally { ImGui.EndTooltip(); }
    }

    private void DrawLighting(bool foreground)
    {
        preview.Draw(settings, foreground, clock.Elapsed.TotalSeconds, runtime.SfxLevel,
            runtime.LimitBreakState);
        var enabled = configuration.Enabled;
        if (ImGui.Checkbox(Label("Enable custom lighting"), ref enabled)) { configuration.Enabled = enabled; Changed(); }
        Help("Use your lighting over USB or Bluetooth while FFXIV is active. Switching apps turns the lights off and releases control.");
        ImGui.BeginDisabled(!enabled);
        try { DrawLightingSettings(foreground); }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawLightingSettings(bool foreground)
    {
        var effect = (int)settings.Effect;
        Section("Lightbar");
        if (ImGui.Combo(Label("Lightbar effect"), ref effect, uiText.Options("Solid\0Rainbow\0Breathing\0Color Shift\0Aurora\0Fire\0Heartbeat\0Comet\0Rainbow Pulse\0SFX Loudness\0SFX Color Blend\0")))
        { configuration.Effect = (LightingEffect)effect; Changed(); }
        var sfx = LightingEffects.IsSfx(settings.Effect);
        var sfxBlend = settings.Effect == LightingEffect.SfxColorBlend;
        if (sfx) Help(sfxBlend ? "The quiet color stays on between sounds." : "Fades to black in silence.");
        if (LightingEffects.UsesPrimaryColor(settings.Effect))
        {
            DrawColor(false, sfxBlend ? "Low SFX color" : "Color");
            if (sfxBlend) Help("Shown during silence and quiet sounds.");
        }
        if (settings.Effect is LightingEffect.ColorShift or LightingEffect.SfxColorBlend)
        {
            DrawColor(true, sfxBlend ? "High SFX color" : "Second color");
            if (sfxBlend) Help("Reached by the loudest sounds.");
        }
        var brightness = settings.Brightness * 100;
        if (ImGui.SliderFloat(Label("Brightness"), ref brightness, 0, 100, "%.0f%%"))
        { configuration.Brightness = brightness / 100; Changed(); }
        Help("Controls the RGB strips; bottom LEDs are unaffected.");
        if (sfx)
        {
            Subsection("Sound response");
            var source = (int)settings.AudioSource;
            if (ImGui.Combo(Label("SFX source"), ref source, uiText.Options("My character only\0All combat sounds\0")))
            { configuration.AudioSource = (SfxSource)source; Changed(); }
            Help(settings.AudioSource == SfxSource.PlayerOnly
                ? "Follows sounds from your character's actions and weapon."
                : "Includes combat sounds from nearby players and enemies.");
            var sensitivity = settings.SfxSensitivity;
            if (ImGui.SliderFloat(Label("SFX sensitivity"), ref sensitivity, 1, 32, "%.1fx"))
            { configuration.SfxSensitivity = sensitivity; Changed(); }
            Help("Raise for quiet skills; lower if the meter stays full.");
            var level = foreground && settings.Enabled ? runtime.SfxLevel : 0;
            ImGui.ProgressBar(level, new Vector2(ImGui.CalcItemWidth(), 0), level.ToString("P0"));
            ImGui.SameLine();
            ImGui.TextUnformatted(T("SFX light level"));
            var release = settings.SfxReleaseSeconds;
            if (ImGui.SliderFloat(Label(sfxBlend ? "Color return (seconds)" : "SFX fade (seconds)"), ref release, 0.05f, 1, "%.2f"))
            { configuration.SfxReleaseSeconds = release; Changed(); }
            Help(sfxBlend ? "How quickly the light returns to the Low color after a sound." : "How quickly the light fades after a sound.");
            if (ImGui.CollapsingHeader(Label("Advanced SFX settings")))
            {
                var automatic = settings.SfxAutomaticGate;
                if (ImGui.Checkbox(Label("Automatic noise gate"), ref automatic))
                { configuration.SfxAutomaticGate = automatic; Changed(); }
                Help("Filters faint sounds and adapts to sensitivity.");
                ImGui.BeginDisabled(automatic);
                try
                {
                    var gate = settings.SfxNoiseGate;
                    if (ImGui.SliderFloat(Label("Manual noise gate"), ref gate, 0, 0.05f, "%.3f"))
                    { configuration.SfxNoiseGate = gate; Changed(); }
                    Help("Ignores sounds below this level. High values can miss quiet skills.");
                }
                finally { ImGui.EndDisabled(); }
            }
        }
        else if (settings.Effect != LightingEffect.Solid)
        {
            var cycle = settings.CycleSeconds;
            if (ImGui.SliderFloat(Label("Lightbar cycle (seconds)"), ref cycle, 1, 60, "%.1f"))
            { configuration.CycleSeconds = cycle; Changed(); }
        }
        Section("Bottom LEDs");
        var indicatorMode = (int)settings.PlayerMode;
        if (ImGui.Combo(Label("LED mode"), ref indicatorMode, uiText.Options("Custom animation\0Limit Break\0")))
        { configuration.PlayerMode = (PlayerIndicatorMode)indicatorMode; Changed(); }
        if (settings.PlayerMode == PlayerIndicatorMode.LimitBreak)
            Help("PvE: ready party bars. PvP: your charge. Flashes when you use your LB.");
        if (settings.PlayerMode == PlayerIndicatorMode.LimitBreak) DrawLimitBreakSettings();
        else
        {
            var player = PlayerIndicatorEffects.Count(settings.PlayerLights);
            if (ImGui.Combo(Label("LED count"), ref player, uiText.Options("Off\0One LED\0Two LEDs\0Three LEDs\0Four LEDs\0Five LEDs\0")))
            { configuration.PlayerLights = PlayerIndicatorEffects.MaskForCount(player); Changed(); }
            if (player != 0)
                Help("Chase, Blink and Loading use the chosen positions. Bounce, Sparkle and Heartbeat use the count.");
            ImGui.BeginDisabled(player == 0);
            try { DrawBottomLedAnimation(player); }
            finally { ImGui.EndDisabled(); }
        }
    }

    private void DrawBottomLedAnimation(int player)
    {
        var animation = (int)settings.PlayerAnimation;
        if (ImGui.Combo(Label("LED animation"), ref animation, uiText.Options("Steady\0Chase\0Bounce\0Blink\0Heartbeat\0Sparkle\0Loading\0")))
        { configuration.PlayerAnimation = (PlayerIndicatorAnimation)animation; Changed(); }
        if (settings.PlayerAnimation == PlayerIndicatorAnimation.Bounce && player == 5)
            Help("Choose fewer than five lights to see the group bounce.");
        else if (settings.PlayerAnimation == PlayerIndicatorAnimation.Chase && player == 1)
            Help("Choose two or more LEDs to see movement.");
        if (settings.PlayerAnimation != PlayerIndicatorAnimation.Steady)
        {
            var cycle = settings.PlayerCycleSeconds;
            if (ImGui.SliderFloat(Label("LED cycle (seconds)"), ref cycle, 1, 12, "%.1f"))
            { configuration.PlayerCycleSeconds = cycle; Changed(); }
        }
    }

    private void DrawLimitBreakSettings()
    {
        var cycle = settings.PlayerCycleSeconds;
        if (ImGui.SliderFloat(Label("LB LED cycle (seconds)"), ref cycle, 1, 12, "%.1f"))
        { configuration.PlayerCycleSeconds = cycle; Changed(); }
        Help("Controls LB blink and loading. The cast/use flash keeps its own timing.");
    }

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
        var measuring = runtime.MeasureUpdates;
        if (ImGui.Checkbox(Label("Measure framework updates"), ref measuring)) measureUpdates(measuring);
        if (runtime.UpdateSamples == 0) ImGui.TextDisabled(T("Framework timing: not collected"));
        else ImGui.TextUnformatted(F("Framework updates: {0}; average {1:F3} ms; maximum {2:F3} ms",
            runtime.UpdateSamples, runtime.UpdateTotalTicks * 1000d / Stopwatch.Frequency / runtime.UpdateSamples,
            runtime.UpdateMaxTicks * 1000d / Stopwatch.Frequency));
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


    internal void RequestTraceToggle()
    {
        selectDiagnostics = true;
        if (!recording.Active && !configuration.CanRecordDiagnostics())
        {
            if (!configuration.HasCompletedSetup) showGeneralInfo = true;
            else showRecordingNotice = true;
            Open();
            return;
        }
        recording.RequestToggle();
    }

    // UI and commands only post intent. The framework owns the recorder, final
    // native snapshot, and save queue, so a segment cannot mutate while saving.







    internal void Open()
    {
        settingsWindow.IsOpen = true;
        if (dialogWindow.IsOpen) dialogWindow.BringToFront();
        else settingsWindow.BringToFront();

        if (!configuration.HasCompletedSetup) showGeneralInfo = true;
    }

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
            saves.MarkDirty(clock.Elapsed.TotalSeconds);
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
                PersistSetupChoice();
                noticeError = null;
                CloseDialog();
            }
            catch (Exception error)
            {
                if (!configuration.HasCompletedSetup)
                {
                    configuration.HasCompletedSetup = false;
                    saves.MarkDirty(clock.Elapsed.TotalSeconds);
                }
                noticeError = error.Message;
                reportError(error);
            }
        }
    }

    private void PersistSetupChoice()
    {
        var previous = (configuration.Enabled, configuration.HapticEnabled, configuration.HasSeenGeneralInfo,
            configuration.HasCompletedSetup);
        configuration.CompleteSetup(setupLighting, setupHaptics);
        try { Save(); }
        catch
        {
            configuration.Enabled = previous.Enabled; configuration.HapticEnabled = previous.HapticEnabled;
            configuration.HasSeenGeneralInfo = previous.HasSeenGeneralInfo;
            configuration.HasCompletedSetup = previous.HasCompletedSetup;
            throw;
        }
        // Persist explicit consent before publishing enabled snapshots or hooks.
        publishSavedSetup();
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

    private bool TraceFilesBusy => recording.Busy;



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

    private void DrawHaptics()
    {
        ImGui.TextWrapped(T("Detailed DualSense haptics require a USB cable. Bluetooth supports RGB lighting and basic rumble."));
        ImGui.Spacing();
        var enabled = configuration.HapticEnabled;
        if (ImGui.Checkbox(Label("Enable haptics"), ref enabled))
        { configuration.HapticEnabled = enabled; Changed(); }
        Help("The game's own vibration is unaffected.");
        ImGui.BeginDisabled(!enabled);
        try
        {
            if (ImGui.CollapsingHeader(Label("Vibration output"))) DrawHapticOutput();
            Section("Combat");
            DrawCombatHaptics();
            Section("Screen effects");
            DrawScreenEffectHaptics();
            Section("Footstep amplification");
            var footstepsAvailable = HapticOutputSelection.SupportsFootstepAmplification(
                configuration.HapticOutput, session.HasUsb, session.HasBluetooth);
            ImGui.BeginDisabled(!footstepsAvailable);
            try { DrawFootstepHaptics(); }
            finally { ImGui.EndDisabled(); }
            if (!footstepsAvailable)
                ImGui.TextWrapped(T("Connect a DualSense or DualSense Edge by USB and select Automatic or DualSense haptics."));
        }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawHapticOutput()
    {
        var mode = (int)(Enum.IsDefined(configuration.HapticOutput) ? configuration.HapticOutput : HapticOutputMode.Automatic);
        if (ImGui.Combo(Label("Output mode"), ref mode,
            uiText.Options("Automatic\0DualSense haptics\0Regular rumble (XInput)\0DualSense Bluetooth rumble\0")))
        { configuration.HapticOutput = (HapticOutputMode)mode; Changed(); }
        Help("Automatic chooses USB DualSense, then Bluetooth DualSense, then XInput.");
        var output = HapticOutputSelection.Resolve(configuration.HapticOutput, session.HasUsb, session.HasBluetooth);
        if (output == HapticOutputMode.XInput)
        {
            var controller = Math.Clamp(configuration.RumbleController + 1, 0, 4);
            if (ImGui.Combo(Label("Rumble controller"), ref controller,
                uiText.Options("First connected\0Controller 1\0Controller 2\0Controller 3\0Controller 4\0")))
            { configuration.RumbleController = controller - 1; Changed(); }
            Help("XInput uses motor-frequency balance. DirectInput devices need a mapper.");
        }
        if (output == HapticOutputMode.DualSense) return;
        Subsection("Basic rumble");
        DrawBasicRumbleSettings(output == HapticOutputMode.Bluetooth);
    }

    private void DrawBasicRumbleSettings(bool bluetooth)
    {
        var snapshot = hapticSettings;
        var strength = snapshot.RumbleStrength * 100;
        if (ImGui.SliderFloat(Label("Rumble strength"), ref strength, 0, 100, "%.0f%%"))
        { configuration.RumbleStrength = strength / 100; Changed(); }
        Help("Caps the strength of added combat rumble.");
        var milliseconds = snapshot.RumblePulseMilliseconds;
        if (ImGui.SliderInt(Label("Pulse duration"), ref milliseconds, RumblePulse.MinimumMilliseconds, RumblePulse.MaximumMilliseconds, "%d ms"))
        { configuration.RumblePulseMilliseconds = milliseconds; Changed(); }
        Help("Longer pulses feel heavier. Sustained sounds do not keep the motors buzzing.");
        if (bluetooth)
        {
            var balance = snapshot.BluetoothMotorBalance * 100;
            if (ImGui.SliderFloat(Label("Bluetooth motor balance"), ref balance, -100, 100, "%+.0f%%"))
            { configuration.BluetoothMotorBalance = balance / 100; Changed(); }
            Help("Positive values soften the left motor; negative values soften the right. The motors have different textures.");
        }
        ImGui.BeginDisabled(runtime.RumbleSlot < 0 || strength <= 0);
        try
        {
            if (ImGui.Button(Label("Test rumble strength"))) requestRumbleTest(1);
        }
        finally { ImGui.EndDisabled(); }
        Help("Plays a weak pulse, then a strong one. Other added vibration pauses during the test.");
        ImGui.BeginDisabled(runtime.RumbleSlot < 0 || strength <= 0);
        try
        {
            if (ImGui.Button(Label("Test left/right motors"))) requestRumbleTest(2);
        }
        finally { ImGui.EndDisabled(); }
        Help("Plays left, then right, using your current balance. Commands remain visible in Diagnostics.");
    }

    private void DrawCombatHaptics()
    {
        var combat = configuration.CombatHaptics;
        if (ImGui.Checkbox(Label("Enable combat haptics"), ref combat))
        { configuration.CombatHaptics = combat; Changed(); }
        ImGui.Indent();
        ImGui.BeginDisabled(!combat);
        try
        {
            var player = configuration.CharacterHaptics;
            if (ImGui.Checkbox(Label("Player SFX based"), ref player))
            { configuration.CharacterHaptics = player; Changed(); }
            Help("Follows your skill and weapon sounds.");
            ImGui.BeginDisabled(!player);
            try
            {
                var strength = hapticSettings.Strength * 100;
                if (ImGui.SliderFloat(Label("Player vibration strength"), ref strength, 0, 100, "%.0f%%"))
                { configuration.HapticStrength = strength / 100; Changed(); }
                if (ImGui.CollapsingHeader(Label("Player SFX settings"))) DrawPlayerImpactSettings();
            }
            finally { ImGui.EndDisabled(); }

            Subsection("Enemy SFX based");
            DrawEnemySfxHaptics();
            ImGui.Spacing();
            if (ImGui.CollapsingHeader(Label("Advanced combat settings"))) DrawCombatSpatialSettings();
        }
        finally { ImGui.EndDisabled(); ImGui.Unindent(); }
    }

    private void DrawEnemySfxHaptics()
    {
        var bosses = configuration.HapticBossEnabled;
        if (ImGui.Checkbox(Label("Boss skills"), ref bosses))
        { configuration.HapticBossEnabled = bosses; Changed(); }
        Help("During duty boss fights, also includes NPC casting, warning cues and basic attacks.");
        var regular = configuration.HapticRegularEnemyEnabled;
        if (ImGui.Checkbox(Label("Regular enemy skills"), ref regular))
        { configuration.HapticRegularEnemyEnabled = regular; Changed(); }
        Help("Excludes basic attacks. Enemies seen before a boss fight stay in this category.");
        var hits = configuration.HapticHitReactions;
        if (ImGui.Checkbox(Label("Player hit reactions"), ref hits))
        { configuration.HapticHitReactions = hits; Changed(); }
        Help("Includes basic attacks. Uses Enemy vibration strength.");
        ImGui.BeginDisabled(!bosses && !regular && !hits);
        try
        {
            var strength = hapticSettings.EnemyStrength * 100;
            if (ImGui.SliderFloat(Label("Enemy vibration strength"), ref strength, 0, 100, "%.0f%%"))
            { configuration.HapticEnemyStrength = strength / 100; Changed(); }
            Help("Also controls player hit reactions.");
        }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawCombatSpatialSettings()
    {
        Help("Also affects enemy skills and player hit reactions.");
        Subsection("Spatial feedback");
        var spatial = configuration.SpatialHaptics;
        if (ImGui.Checkbox(Label("Spatial haptics"), ref spatial))
        { configuration.SpatialHaptics = spatial; Changed(); }
        Help("Uses your position and the camera's facing to balance the grips.");

        Subsection("Distance");
        var distance = configuration.HapticDistanceFalloff;
        if (ImGui.Checkbox(Label("Distance fade"), ref distance))
        { configuration.HapticDistanceFalloff = distance; Changed(); }
        Help("Sounds without a known position are skipped while enabled.");
        ImGui.BeginDisabled(!distance);
        try { DrawHapticDistanceSettings(); }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawPlayerImpactSettings()
    {
        ImGui.BeginDisabled(!configuration.SpatialHaptics);
        try
        {
            var follow = configuration.HapticFollowImpact;
            if (ImGui.Checkbox(Label("Follow skill impacts"), ref follow))
            { configuration.HapticFollowImpact = follow; Changed(); }
            Help("Steers cast and weapon vibration toward the skill's impact. Requires Spatial haptics.");
            ImGui.BeginDisabled(!follow);
            try
            {
                var bias = hapticSettings.ImpactBias * 100;
                if (ImGui.SliderFloat(Label("Impact bias"), ref bias, HapticSettings.MinimumImpactBias * 100, 100, "%.0f%%"))
                { configuration.HapticImpactBias = bias / 100; Changed(); }
                Help("Higher values follow the impact more closely. Strength is unchanged.");
            }
            finally { ImGui.EndDisabled(); }
        }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawScreenEffectHaptics()
    {
        var enabled = configuration.HapticScreenEffects;
        if (ImGui.Checkbox(Label("Enable screen effect haptics"), ref enabled))
        { configuration.HapticScreenEffects = enabled; Changed(); }
        Help("Follows camera shake, blur and color-fringe effects. Pauses in PvP.");
        ImGui.BeginDisabled(!enabled);
        try
        {
            var strength = hapticSettings.ScreenStrength * 100;
            if (ImGui.SliderFloat(Label("Screen effect vibration strength"), ref strength, 0, 100, "%.0f%%"))
            { configuration.HapticScreenStrength = strength / 100; Changed(); }
        }
        finally { ImGui.EndDisabled(); }
        ImGui.TextDisabled(T("Plays alongside other vibration; combined strength is limited."));
        ImGui.TextDisabled(T("Centered feedback; no distance fade."));
    }

    private void DrawFootstepHaptics()
    {
        var footsteps = configuration.FootstepAmplifier;
        if (ImGui.Checkbox(Label("Amplify native footstep vibration"), ref footsteps))
        { configuration.FootstepAmplifier = footsteps; Changed(); }
        Help("Amplifies existing footsteps while unmounted. Requires USB and in-game vibration.");
        ImGui.BeginDisabled(!footsteps);
        try
        {
            var multiplier = hapticSettings.FootstepMultiplier * 100;
            if (ImGui.SliderFloat(Label("Footstep vibration"), ref multiplier, 100, HapticSettings.MaximumFootstepMultiplier * 100, "%.0f%%"))
            { configuration.FootstepMultiplier = multiplier / 100; Changed(); }
            Help("100% is native strength. Mount and speaker effects are unaffected.");
        }
        finally { ImGui.EndDisabled(); }
        ImGui.TextDisabled(T("Native footstep strength varies by race."));
    }

    private void DrawHapticDistanceSettings()
    {
        var origin = (int)hapticSettings.DistanceOrigin;
        if (ImGui.Combo(Label("Distance origin"), ref origin, uiText.Options("Character (recommended)\0Camera\0")))
        { configuration.HapticDistanceSource = (HapticDistanceOrigin)origin; Changed(); }
        var automatic = configuration.HapticAutomaticDistance;
        var snapshot = hapticSettings;
        var fromCamera = snapshot.DistanceOrigin == HapticDistanceOrigin.Camera;
        Help(fromCamera
            ? "Uses the camera's distance to the sound. Zoom affects the fade."
            : "Uses your character's distance to the sound, regardless of zoom.");
        if (ImGui.Checkbox(Label("Use recommended distance fade"), ref automatic))
        { configuration.HapticAutomaticDistance = automatic; Changed(); }
        Help(fromCamera
            ? F("Adds camera distance to the {0:F0}–{1:F0} yalm range.", HapticSettings.RecommendedNearDistance, HapticSettings.RecommendedFarDistance)
            : F("Full strength within {0:F0} yalms; silent at {1:F0}.", HapticSettings.RecommendedNearDistance, HapticSettings.RecommendedFarDistance));
        ImGui.BeginDisabled(automatic);
        try
        {
            snapshot = hapticSettings;
            var near = snapshot.NearDistance;
            var far = snapshot.FarDistance;
            if (ImGui.SliderFloat(Label("Full strength within (yalms)"), ref near, 0, far - 1, "%.1f"))
            {
                if (fromCamera) configuration.HapticNearDistance = near;
                else configuration.HapticCharacterNearDistance = near;
                Changed();
            }
            if (ImGui.SliderFloat(Label("Fade to zero at (yalms)"), ref far, near + 1, 100, "%.1f"))
            {
                if (fromCamera) configuration.HapticFarDistance = far;
                else configuration.HapticCharacterFarDistance = far;
                Changed();
            }
        }
        finally { ImGui.EndDisabled(); }
    }


}
