// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;
using DualSenseLighting.Ui;

[assembly: System.Reflection.AssemblyVersion("0.13.3.0")]
[assembly: System.Reflection.AssemblyTitle("PS5 DualSense Controller for FFXIV")]
[assembly: System.Reflection.AssemblyProduct("PS5 DualSense Controller for FFXIV")]

namespace DualSenseLighting;

public sealed class Plugin : IDalamudPlugin
{
    private static readonly string PluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    private const string Command = "/ds5";
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly Configuration configuration;
    private readonly UiText uiText = new();
    private readonly IFontHandle uiFont;
    private readonly CommandInfo commandInfo;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource stop = new();
    private readonly LightingSession session;
    private readonly GameOutputGuard guard;
    private readonly Task worker;
    private readonly ControllerPreview preview;
    private readonly IGameInteropProvider interop;
    private readonly ISigScanner scanner;
    private readonly IObjectTable objects;
    private readonly IDataManager data;
    private readonly IClientState clientState;
    private Task<GameLimitBreak>? limitBreakInitialization;
    private GameLimitBreak? limitBreakCapture;
    private LimitBreakState limitBreakState = LimitBreakState.Empty;
    private string? limitBreakError;
    private readonly SfxEnvelope sfxEnvelope = new();
    private SfxAudioCapture? audioCapture;
    private Task<SfxAudioCapture>? audioInitialization;
    private string? audioError;
    private float sfxLevel, sfxRms;
    private LightingSettings settings;
    private HapticSettings hapticSettings;
    private bool windowOpen;
    private bool showGeneralInfo;
    private double saveAt = double.PositiveInfinity;
    private string? lastError;
    private bool reportedGuardError;
    private SfxTrace? sfxTrace;
    private string? traceStatus;
    private string? traceError;
    private bool selectDiagnostics;
    private bool commandRegistered;
    private int disposed;
    private long hapticEnableCueDeadline;
    private enum ResetTarget { Lighting, Haptics }
    private ResetTarget? resetTarget;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands,
        IFramework framework, IGameInteropProvider interop, IPluginLog log, ITextureProvider textures, ISigScanner scanner,
        IObjectTable objects, IDataManager data, IClientState clientState, IChatGui chat)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        this.log = log;
        this.chat = chat;
        this.interop = interop;
        this.scanner = scanner;
        this.objects = objects;
        this.data = data;
        this.clientState = clientState;
        if (pluginInterface.InstalledPlugins.Any(p => p.InternalName == "SteamLightingGuard" && p.IsLoaded))
            throw new InvalidOperationException("Disable the older Steam Lighting Guard before loading PS5 DualSense Controller for FFXIV.");
        configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        uiText.Language = Enum.IsDefined(configuration.Language) ? configuration.Language : UiLanguage.English;
        settings = configuration.Snapshot();
        hapticSettings = configuration.HapticsSnapshot();
        try
        {
            uiFont = pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
                e.OnPreBuild(tk => tk.AddDalamudDefaultFont(-1, UiText.GlyphRanges)));
            preview = new ControllerPreview(textures, uiText);
            session = new LightingSession(new HidDevices(), Native.IsCurrentProcessForeground, ReportError);
            guard = new GameOutputGuard(interop, () => Volatile.Read(ref settings), () => clock.Elapsed.TotalSeconds,
                () => Volatile.Read(ref sfxLevel), () => Volatile.Read(ref limitBreakState));
            commandInfo = new CommandInfo(OnCommand) { HelpMessage = T("Configure lighting and haptics. Use /ds5 help for feature commands.") };
            commandRegistered = commands.AddHandler(Command, commandInfo);
            if (!commandRegistered) throw new InvalidOperationException(T("The /ds5 command is already registered by another plugin."));
            pluginInterface.UiBuilder.Draw += Draw;
            pluginInterface.UiBuilder.OpenConfigUi += Open;
            pluginInterface.UiBuilder.OpenMainUi += Open;
            framework.Update += Update;
            worker = Task.Run(RunWorker);
            log.Information("PS5 DualSense Controller for FFXIV {Version} enabled. USB lighting is foreground-only; background game LED commands are guarded.", PluginVersion);
        }
        catch
        {
            if (commandRegistered) commands.RemoveHandler(Command);
            pluginInterface.UiBuilder.Draw -= Draw;
            pluginInterface.UiBuilder.OpenConfigUi -= Open;
            pluginInterface.UiBuilder.OpenMainUi -= Open;
            framework.Update -= Update;
            stop.Cancel();
            worker?.GetAwaiter().GetResult();
            DisposeInitialization(audioInitialization);
            DisposeInitialization(limitBreakInitialization);
            guard?.Dispose();
            session?.Dispose();
            stop.Dispose();
            uiFont?.Dispose();
            throw;
        }
    }

    private async Task RunWorker()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
            do
            {
                var snapshot = Volatile.Read(ref settings);
                var seconds = clock.Elapsed.TotalSeconds;
                var capture = Volatile.Read(ref audioCapture);
                var foreground = Native.IsCurrentProcessForeground();
                var requestedHaptics = Volatile.Read(ref hapticSettings);
                if (!foreground || !requestedHaptics.Enabled || Stopwatch.GetTimestamp() >= Interlocked.Read(ref hapticEnableCueDeadline))
                    Interlocked.Exchange(ref hapticEnableCueDeadline, 0);
                capture?.Haptics?.Configure(requestedHaptics, foreground,
                    capture.PlayerFilterSupported, capture.LayoutSupported && !capture.Faulted);
                if (capture != null) capture.UpdateEnableCue(Interlocked.Exchange(ref hapticEnableCueDeadline, 0) > Stopwatch.GetTimestamp());
                if (capture?.SetSource(snapshot.AudioSource) == true)
                    sfxEnvelope.Update(0, seconds, snapshot, false);
                var active = snapshot.Enabled && LightingEffects.IsSfx(snapshot.Effect) &&
                    foreground && capture != null && !capture.Faulted && capture.LayoutSupported &&
                    (snapshot.AudioSource == SfxSource.AllCombat || capture.PlayerFilterSupported);
                capture?.Loudness.SetActive(active);
                var rms = active ? capture!.Loudness.TakePeak() : 0;
                Volatile.Write(ref sfxRms, rms);
                var level = sfxEnvelope.Update(rms, seconds, snapshot, active);
                Volatile.Write(ref sfxLevel, level);
                await session.TickAsync(snapshot, seconds, stop.Token, level, Volatile.Read(ref limitBreakState)).ConfigureAwait(false);
            } while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { ReportError(error); }
        finally
        {
            Volatile.Read(ref audioCapture)?.Loudness.SetActive(false);
            Volatile.Read(ref audioCapture)?.Haptics?.Configure(new HapticSettings(), false, false);
            Volatile.Read(ref audioCapture)?.UpdateEnableCue(false);
            Volatile.Write(ref sfxLevel, 0);
            await session.ReleaseAsync(true, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void ReportError(Exception error)
    {
        var message = error.Message;
        if (Interlocked.Exchange(ref lastError, message) != message)
            log.Warning(error, "DualSense lighting output paused; reconnecting devices will be retried.");
    }

    private void OnCommand(string command, string args)
    {
        var parsed = PluginCommand.Parse(args);
        switch (parsed.Kind)
        {
            case PluginCommandKind.Open: Open(); return;
            case PluginCommandKind.Info: showGeneralInfo = true; Open(); return;
            case PluginCommandKind.Trace: StartTrace(); Open(); return;
            case PluginCommandKind.Help: PrintCommandHelp(); return;
            case PluginCommandKind.Invalid:
                chat.PrintError("[PS5 DualSense] " + T("Unknown command or value. Use /ds5 help for syntax."));
                return;
            case PluginCommandKind.Switch: break;
            default: return;
        }
        if (configuration.SetFeature(parsed.Feature, parsed.Switch)) Changed();
        var enabled = configuration.IsFeatureEnabled(parsed.Feature);
        var label = parsed.Feature switch
        {
            ControllerFeature.Lighting => "Custom lighting",
            ControllerFeature.CombatHaptics => "Combat haptics",
            ControllerFeature.Footsteps => "Amplify native footstep vibration",
            ControllerFeature.SpatialHaptics => "Spatial haptics",
            ControllerFeature.DistanceFade => "Distance fade",
            _ => "Follow skill impacts",
        };
        chat.Print("[PS5 DualSense] " + F("{0}: {1}", T(label), T(enabled ? "On" : "Off")));
        if (enabled && parsed.Feature is ControllerFeature.SpatialHaptics or ControllerFeature.DistanceFade or ControllerFeature.FollowImpact)
        {
            if (!configuration.CharacterHaptics)
                chat.Print("[PS5 DualSense] " + T("Saved. Enable combat haptics to use this setting."));
            else if (parsed.Feature != ControllerFeature.SpatialHaptics && !configuration.SpatialHaptics)
                chat.Print("[PS5 DualSense] " + T("Saved. Enable spatial haptics to use this setting."));
        }
    }

    private void PrintCommandHelp()
    {
        chat.Print("[PS5 DualSense] " + T("/ds5 opens settings. /ds5 info opens the guide. /ds5 trace records diagnostics."));
        chat.Print(T("Usage: /ds5 <feature> <on|off|toggle>"));
        chat.Print(T("Features: lighting, combathaptics, footsteps, spatialhaptics, distancefade, followimpact."));
    }

    private void StartTrace()
    {
        selectDiagnostics = true;
        traceError = null;
        var capture = Volatile.Read(ref audioCapture);
        if (capture == null) { traceStatus = "Select an SFX effect or enable combat haptics, then wait for capture to be ready."; return; }
        sfxTrace = new SfxTrace(PluginVersion);
        capture.SetTracing(true);
        traceStatus = null;
        log.Information("SFX diagnostic recording started: 15 seconds.");
    }
    private void Open() => windowOpen = true;

    private unsafe void Update(IFramework _)
    {
        // Scanning a large executable must not block a game frame. The optional observer
        // remains inactive until its completed initialization is published to the worker.
        var snapshot = Volatile.Read(ref settings);
        var lbEnabled = snapshot.Enabled && snapshot.PlayerMode == PlayerIndicatorMode.LimitBreak;
        if (lbEnabled && limitBreakInitialization == null)
            limitBreakInitialization = Task.Run(() => new GameLimitBreak(interop, scanner, data, () => clock.Elapsed.TotalSeconds));
        if (limitBreakInitialization?.IsCompleted == true && limitBreakCapture == null && limitBreakError == null)
        {
            try { limitBreakCapture = limitBreakInitialization.GetAwaiter().GetResult(); }
            catch (Exception error)
            {
                limitBreakError = error.Message;
                log.Warning(error, "Limit Break indicators are unavailable; other lighting remains available.");
            }
        }
        var lb = limitBreakCapture?.Read(objects.LocalPlayer, clientState.TerritoryType, clientState.IsPvP,
            clock.Elapsed.TotalSeconds, lbEnabled && Native.IsCurrentProcessForeground()) ?? LimitBreakState.Empty;
        Volatile.Write(ref limitBreakState, lb);
        if (audioInitialization == null && (snapshot.Enabled && LightingEffects.IsSfx(snapshot.Effect) || Volatile.Read(ref hapticSettings).NeedsAudio))
            audioInitialization = Task.Run(() => new SfxAudioCapture(interop, scanner));
        if (audioInitialization?.IsCompleted == true && Volatile.Read(ref audioCapture) == null && audioError == null)
        {
            try { Volatile.Write(ref audioCapture, audioInitialization.GetAwaiter().GetResult()); }
            catch (Exception error)
            {
                audioError = error.Message;
                log.Warning(error, "SFX lighting prototype unavailable; other lighting modes remain available.");
            }
        }
        var capture = Volatile.Read(ref audioCapture);
        if (capture != null)
        {
            // ObjectTable is read on the framework thread only. The draw-object field
            // follows the pinned GameObject layout; callbacks receive an immutable identity.
            var player = objects.LocalPlayer;
            var address = player?.Address ?? 0;
            capture.SetPlayer(address, player?.EntityId ?? 0, address == 0 ? 0 : *(nint*)(address + 0x100), player?.Position,
                player?.CurrentMount != null);
            if (sfxTrace != null)
            {
                try
                {
                    if (sfxTrace.Sample(capture))
                    {
                        capture.SetTracing(false);
                        var path = Path.Combine(pluginInterface.AssemblyLocation.DirectoryName!, "sfx-trace.json");
                        sfxTrace.Save(path);
                        traceStatus = "Recording complete — you can stop casting.";
                        sfxTrace = null;
                        log.Information("SFX diagnostic recording complete: {Path}", path);
                    }
                }
                catch (Exception error)
                {
                    capture.SetTracing(false); sfxTrace = null;
                    traceStatus = "Recording stopped: {0}";
                    traceError = error.Message;
                    log.Warning(error, "SFX diagnostic recording stopped.");
                }
            }
        }
        if (clock.Elapsed.TotalSeconds >= saveAt) Save();
        if (!reportedGuardError && guard.Errors > 0)
        {
            reportedGuardError = true;
            log.Warning("A controller output could not be inspected. Original game output was passed through.");
        }
    }

    private void Changed()
    {
        Volatile.Write(ref settings, configuration.Snapshot());
        var previous = Volatile.Read(ref hapticSettings);
        var current = configuration.HapticsSnapshot();
        Volatile.Write(ref hapticSettings, current);
        if (!previous.Enabled && current.Enabled)
            Interlocked.Exchange(ref hapticEnableCueDeadline, Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2);
        else if (!current.Enabled) Interlocked.Exchange(ref hapticEnableCueDeadline, 0);
        saveAt = clock.Elapsed.TotalSeconds + 0.5;
    }

    private void Save()
    {
        pluginInterface.SavePluginConfig(configuration);
        saveAt = double.PositiveInfinity;
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

    private void Draw()
    {
        if (!windowOpen) return;
        using var font = uiFont.Push();
        var width = 560 * ImGui.GetFontSize() / 17f;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, float.MaxValue));
        if (!ImGui.Begin("PS5 DualSense Controller for FFXIV###DualSenseLightingConfig", ref windowOpen, ImGuiWindowFlags.AlwaysAutoResize))
        { ImGui.End(); return; }
        ImGui.PushTextWrapPos(0);
        ImGui.PushItemWidth(ImGui.GetFontSize() * 14);
        try
        {
            var showInfo = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.InfoCircle, Label("Info"));
            if (ImGui.IsItemHovered()) DrawTooltip("Controller compatibility, USB setup and how the plugin works.");
            ImGui.SameLine();
            DrawLanguageSelector();
            if (showInfo || showGeneralInfo || !configuration.HasSeenGeneralInfo)
            {
                ImGui.OpenPopup(GeneralInfoPopupTitle());
                showGeneralInfo = false;
            }
            var foreground = Native.IsCurrentProcessForeground();
            ResetTarget? activeSettings = null;
            if (ImGui.BeginTabBar("DualSenseSettingsTabs"))
            {
                try
                {
                    if (ImGui.BeginTabItem(Label("Lighting")))
                    {
                        activeSettings = ResetTarget.Lighting;
                        try { DrawLighting(foreground); }
                        finally { ImGui.EndTabItem(); }
                    }
                    if (ImGui.BeginTabItem(Label("Haptics")))
                    {
                        activeSettings = ResetTarget.Haptics;
                        try { DrawHaptics(); }
                        finally { ImGui.EndTabItem(); }
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
            if (ImGui.Button(Label("Close"))) windowOpen = false;
            Help("Your changes are saved automatically and restored when you reconnect.");
            if (activeSettings.HasValue)
            {
                ImGui.SameLine();
                if (ImGui.Button(Label("Reset to defaults")))
                {
                    resetTarget = activeSettings.Value;
                    ImGui.OpenPopup(ResetPopupTitle());
                }
                Help("Restore defaults for this tab.");
            }
            DrawResetConfirmation();
            DrawGeneralInfo();
        }
        finally { ImGui.PopItemWidth(); ImGui.PopTextWrapPos(); ImGui.End(); }
    }

    private string ResetPopupTitle() =>
        T(resetTarget == ResetTarget.Lighting ? "Reset lighting?" : "Reset haptics?") + "###DualSenseResetDefaults";

    private string GeneralInfoPopupTitle() => Label("Before you start");

    private void DrawGeneralInfo()
    {
        var title = GeneralInfoPopupTitle();
        if (!ImGui.IsPopupOpen(title)) return;
        var width = 500 * ImGui.GetFontSize() / 17f;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, float.MaxValue));
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2,
            ImGuiCond.Appearing, new Vector2(0.5f));
        // Use the same focused popup as our confirmations, without a full-screen tint.
        if (!ImGui.BeginPopup(title, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings)) return;
        try
        {
            if (!configuration.HasSeenGeneralInfo)
            {
                configuration.HasSeenGeneralInfo = true;
                saveAt = clock.Elapsed.TotalSeconds + 0.5;
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) { ImGui.CloseCurrentPopup(); return; }
            var closeWidth = ImGui.CalcTextSize(T("Close")).X + ImGui.GetFontSize();
            var closeX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - closeWidth;
            ImGui.TextUnformatted(T("Before you start"));
            ImGui.SameLine(closeX);
            if (ImGui.SmallButton(Label("Close"))) { ImGui.CloseCurrentPopup(); return; }
            Divider();
            ImGui.Spacing();
            ImGui.TextWrapped(PluginInfo.Name);
            ImGui.Spacing();
            DrawLanguageSelector();
            DrawInfoItem("Supported controllers", "Use a Sony DualSense or DualSense Edge (PS5). Other controllers are not supported.");
            DrawInfoItem("USB connection", "Connect your controller with a USB cable. Lighting and vibration do not work over Bluetooth.");
            DrawInfoItem("Game settings", "Enable controller vibration in FFXIV to use haptics. Set up controller-speaker effects in the game as usual.");
            DrawInfoItem("Switching apps", "Custom lights turn off and added vibration pauses when FFXIV is in the background. They resume when you return.");
            Divider();
            ImGui.Spacing();
            ImGui.TextWrapped(T("Your changes are saved automatically. You can reopen this guide with Info in the top-left corner."));
            ImGui.Spacing();
            if (ImGui.Button(Label("Got it"))) ImGui.CloseCurrentPopup();
            ImGui.SetItemDefaultFocus();
        }
        finally { ImGui.EndPopup(); }
    }

    private void DrawInfoItem(string heading, string description)
    {
        ImGui.TextUnformatted(T(heading));
        ImGui.TextWrapped(T(description));
        ImGui.Spacing();
    }

    private void DrawAbout()
    {
        DrawAboutText(PluginInfo.Name);
        DrawAboutText(F("Version {0} · Public test build", PluginVersion), true);
        DrawAboutText(T("Make FFXIV more immersive with custom DualSense lighting and vibration."));

        Section("Project", true);
        DrawAboutText(F("Author: {0}", PluginInfo.Author));
        DrawAboutText(F("License: {0}", PluginInfo.License));
        DrawAboutText(T("Windows · USB DualSense or DualSense Edge · Dalamud API 15"));
        ImGui.Spacing();
        CenterAboutItem(ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.CodeBranch, T("View source on GitHub")));
        DrawAboutLink("View source on GitHub", FontAwesomeIcon.CodeBranch, PluginInfo.RepositoryUrl,
            "Browse the source code, releases and issue tracker.");

        Section("Support the project", true);
        DrawAboutText(T("Enjoying the plugin? Buy me a coffee."));
        ImGui.Spacing();
        CenterAboutRow("Support on Afdian", FontAwesomeIcon.Bolt, "Support on Ko-fi", FontAwesomeIcon.MugHot);
        DrawAboutLink("Support on Afdian", FontAwesomeIcon.Bolt, PluginInfo.AfdianUrl,
            "Opens the creator's support page in your browser.", PluginInfo.AfdianAvailable);
        ImGui.SameLine();
        DrawAboutLink("Support on Ko-fi", FontAwesomeIcon.MugHot, PluginInfo.KofiUrl,
            "Opens the creator's support page in your browser.", PluginInfo.KofiAvailable);
        ImGui.Spacing();
        DrawAboutText(T("This plugin is completely free."), true);

        Section("Feedback", true);
        DrawAboutText(T("Found a bug or have an idea? I'd love to hear from you."));
        DrawAboutText(PluginInfo.FeedbackEmail);
        ImGui.Spacing();
        CenterAboutRow("Send feedback by email", FontAwesomeIcon.Envelope, "Feedback on GitHub", FontAwesomeIcon.CommentDots);
        DrawAboutLink("Send feedback by email", FontAwesomeIcon.Envelope, PluginInfo.FeedbackUrl,
            "Opens your email app with this plugin's name already in the subject.");
        ImGui.SameLine();
        DrawAboutLink("Feedback on GitHub", FontAwesomeIcon.CommentDots, PluginInfo.GitHubFeedbackUrl,
            "Opens a new GitHub issue. Describe your feedback and submit it when you're ready.");

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

    private void DrawAboutLink(string label, FontAwesomeIcon icon, string? url, string tooltip, bool available = true)
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
            DrawTooltip(available ? tooltip : "This support link will be added soon.");
    }

    private void DrawResetConfirmation()
    {
        if (!resetTarget.HasValue) return;
        var width = 450 * ImGui.GetFontSize() / 17f;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, float.MaxValue));
        // Modal windows tint the entire game with the user's ModalWindowDimBg
        // color. Keep this confirmation in a regular popup without that overlay.
        if (!ImGui.BeginPopup(ResetPopupTitle(), ImGuiWindowFlags.AlwaysAutoResize))
        {
            resetTarget = null;
            return;
        }
        try
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                ImGui.CloseCurrentPopup();
                resetTarget = null;
                return;
            }
            var closeWidth = ImGui.CalcTextSize(T("Close")).X + ImGui.GetFontSize();
            var closeX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - closeWidth;
            ImGui.TextUnformatted(T(resetTarget == ResetTarget.Lighting ? "Reset lighting?" : "Reset haptics?"));
            ImGui.SameLine(closeX);
            if (ImGui.SmallButton(Label("Close")))
            {
                ImGui.CloseCurrentPopup();
                resetTarget = null;
                return;
            }
            Divider();
            ImGui.TextWrapped(T(resetTarget == ResetTarget.Lighting
                ? "Restore the default lighting settings? Your haptics settings and language will stay the same."
                : "Restore the default haptics settings? Your lighting settings and language will stay the same."));
            ImGui.Spacing();
            if (ImGui.Button(Label("Cancel")))
            {
                ImGui.CloseCurrentPopup();
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
                ImGui.CloseCurrentPopup();
                resetTarget = null;
            }
        }
        finally { ImGui.EndPopup(); }
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
            commandInfo.HelpMessage = T("Configure lighting and haptics. Use /ds5 help for feature commands.");
            saveAt = clock.Elapsed.TotalSeconds + 0.5;
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
        preview.Draw(Volatile.Read(ref settings), foreground, clock.Elapsed.TotalSeconds, Volatile.Read(ref sfxLevel),
            Volatile.Read(ref limitBreakState));
        var enabled = configuration.Enabled;
        if (ImGui.Checkbox(Label("Enable custom lighting"), ref enabled)) { configuration.Enabled = enabled; Changed(); }
        Help("Use your lighting while FFXIV is active. Switching apps turns the lights off and releases control. Native controller effects still work. USB only.");
        ImGui.BeginDisabled(!enabled);
        try { DrawLightingSettings(foreground); }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawLightingSettings(bool foreground)
    {
        var effect = (int)settings.Effect;
        Section("Lightbar");
        if (ImGui.Combo(Label("Lightbar effect"), ref effect, uiText.Options("Solid\0Rainbow\0Breathing\0Color Shift\0Aurora\0Fire\0Heartbeat\0Comet\0Rainbow Pulse\0SFX Loudness (Prototype)\0SFX Color Blend (Prototype)\0")))
        { configuration.Effect = (LightingEffect)effect; Changed(); }
        Help(settings.Effect switch
        {
                LightingEffect.Solid => "Hold your chosen color at a constant brightness.",
                LightingEffect.Rainbow => "Cycle smoothly through rainbow colors.",
                LightingEffect.Breathing => "Your color gently fades in and out.",
                LightingEffect.ColorShift => "Blend back and forth between two colors.",
                LightingEffect.Aurora => "Softly shimmer through teal, blue and purple.",
                LightingEffect.Fire => "Flicker between warm reds and golds.",
                LightingEffect.Heartbeat => "Two quick pulses, then a pause.",
                LightingEffect.Comet => "A quick flash that slowly fades.",
                LightingEffect.RainbowPulse => "Rainbow colors brighten and dim three times per cycle.",
                LightingEffect.SfxLoudness => "Your color brightens with combat sounds and fades out in silence. Bottom LEDs work separately.",
                LightingEffect.SfxColorBlend => "Blend from the quiet color to the loud color as sounds get louder. The quiet color stays on between sounds.",
                _ => "",
        });
        var sfx = LightingEffects.IsSfx(settings.Effect);
        var sfxBlend = settings.Effect == LightingEffect.SfxColorBlend;
        if (LightingEffects.UsesPrimaryColor(settings.Effect))
        {
            DrawColor(false, sfxBlend ? "Low SFX color" : "Color");
            Help(sfxBlend ? "The color shown between sounds and during quiet moments." : "The main color used by this effect.");
        }
        if (settings.Effect is LightingEffect.ColorShift or LightingEffect.SfxColorBlend)
        {
            DrawColor(true, sfxBlend ? "High SFX color" : "Second color");
            Help(sfxBlend ? "The color reached by the loudest sounds." : "The second color in the repeating blend.");
        }
        var brightness = settings.Brightness * 100;
        if (ImGui.SliderFloat(Label("Brightness"), ref brightness, 0, 100, "%.0f%%"))
        { configuration.Brightness = brightness / 100; Changed(); }
        Help("How bright the side lights can get. Bottom LEDs are unaffected.");
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
            Help("Turn up to make quiet sounds affect the lights more. Game volume stays the same.");
            var level = foreground && settings.Enabled ? Volatile.Read(ref sfxLevel) : 0;
            ImGui.ProgressBar(level, new Vector2(ImGui.CalcItemWidth(), 0), level.ToString("P0"));
            ImGui.SameLine();
            ImGui.TextUnformatted(T("SFX light level"));
            Help("Use your skills while adjusting sensitivity. Lower it if the meter stays full; raise it if quiet skills barely move it.");
            var release = settings.SfxReleaseSeconds;
            if (ImGui.SliderFloat(Label(sfxBlend ? "Color return (seconds)" : "SFX fade (seconds)"), ref release, 0.05f, 1, "%.2f"))
            { configuration.SfxReleaseSeconds = release; Changed(); }
            Help(sfxBlend ? "How quickly the light returns to the Low color after a sound." : "How quickly the light fades after a sound.");
            if (ImGui.CollapsingHeader(Label("Advanced SFX settings")))
            {
                var automatic = settings.SfxAutomaticGate;
                if (ImGui.Checkbox(Label("Automatic noise gate"), ref automatic))
                { configuration.SfxAutomaticGate = automatic; Changed(); }
                Help("Filter out faint sounds automatically. Turn off to set the threshold yourself.");
                ImGui.BeginDisabled(automatic);
                try
                {
                    var gate = settings.SfxNoiseGate;
                    if (ImGui.SliderFloat(Label("Manual noise gate"), ref gate, 0, 0.05f, "%.3f"))
                    { configuration.SfxNoiseGate = gate; Changed(); }
                    Help("Ignore sounds below this level. Too high may miss quiet skills. Turn off Automatic noise gate to adjust.");
                }
                finally { ImGui.EndDisabled(); }
            }
        }
        else if (settings.Effect != LightingEffect.Solid)
        {
            var cycle = settings.CycleSeconds;
            if (ImGui.SliderFloat(Label("Lightbar cycle (seconds)"), ref cycle, 1, 60, "%.1f"))
            { configuration.CycleSeconds = cycle; Changed(); }
            Help("Seconds per lightbar animation. Bottom LEDs have their own timing.");
        }
        Section("Bottom LEDs");
        var indicatorMode = (int)settings.PlayerMode;
        if (ImGui.Combo(Label("LED mode"), ref indicatorMode, uiText.Options("Custom animation\0Limit Break\0")))
        { configuration.PlayerMode = (PlayerIndicatorMode)indicatorMode; Changed(); }
        Help(settings.PlayerMode == PlayerIndicatorMode.LimitBreak
            ? "The white lights below the touchpad show your Limit Break. In PvE, they show ready party bars. In PvP, they show your charge. They flash quickly when you use your LB."
            : "The five white lights below the touchpad. Choose how many to use and how they animate.");
        if (settings.PlayerMode == PlayerIndicatorMode.LimitBreak) DrawLimitBreakSettings();
        else
        {
            var player = PlayerIndicatorEffects.Count(settings.PlayerLights);
            if (ImGui.Combo(Label("LED count"), ref player, uiText.Options("Off\0One LED\0Two LEDs\0Three LEDs\0Four LEDs\0Five LEDs\0")))
            { configuration.PlayerLights = PlayerIndicatorEffects.MaskForCount(player); Changed(); }
            Help(player == 0 ? "Choose at least one LED to turn on animations."
                : "Chase, Blink and Loading stay within your chosen positions. For Bounce, Sparkle and Heartbeat, the count sets how many LEDs light up.");
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
        Help(settings.PlayerAnimation switch
        {
            PlayerIndicatorAnimation.Steady => "Keep the selected bottom LEDs lit.",
            PlayerIndicatorAnimation.Bounce => player == 5 ? "Choose fewer than five lights to see the group bounce." : "Your selected number of lights bounce together across the strip.",
            PlayerIndicatorAnimation.Sparkle => "Your selected number of lights flash at once across the strip.",
            PlayerIndicatorAnimation.Heartbeat => "Your chosen LEDs pulse twice, then pause.",
            PlayerIndicatorAnimation.Chase => player == 1 ? "Choose two or more LEDs to see movement." : "A light moves through the selected bottom LED positions.",
            PlayerIndicatorAnimation.Blink => "The selected bottom LEDs blink together.",
            PlayerIndicatorAnimation.Loading => "Light up the chosen positions one by one, then start again.",
            _ => "Animation stays within the selected bottom LED positions.",
        });
        if (settings.PlayerAnimation != PlayerIndicatorAnimation.Steady)
        {
            var cycle = settings.PlayerCycleSeconds;
            if (ImGui.SliderFloat(Label("LED cycle (seconds)"), ref cycle, 1, 12, "%.1f"))
            { configuration.PlayerCycleSeconds = cycle; Changed(); }
            Help("Seconds per bottom LED animation. The side lights have their own timing.");
        }
    }

    private void DrawLimitBreakSettings()
    {
        var cycle = settings.PlayerCycleSeconds;
        if (ImGui.SliderFloat(Label("LB LED cycle (seconds)"), ref cycle, 1, 12, "%.1f"))
        { configuration.PlayerCycleSeconds = cycle; Changed(); }
        Help("Seconds per blink or loading cycle. The quick flash when using your LB keeps its own speed.");
    }

    private void DrawLimitBreakDiagnostics(bool foreground)
    {
        var state = Volatile.Read(ref limitBreakState);
        var seconds = clock.Elapsed.TotalSeconds;
        if (settings.PlayerMode != PlayerIndicatorMode.LimitBreak) ImGui.TextDisabled(T("Limit Break mode is not selected."));
        else if (!foreground || !settings.Enabled) ImGui.TextDisabled(T("Limit Break LEDs paused."));
        else if (limitBreakError != null) ImGui.TextWrapped(F("Limit Break detection unavailable: {0}", limitBreakError));
        else if (limitBreakCapture == null) ImGui.TextDisabled(T("Preparing Limit Break detection…"));
        else if (!state.IsFresh(seconds)) ImGui.TextDisabled(T("No active Limit Break gauge. Bottom LEDs are off."));
        else
        {
            var maximum = state.BarUnits * state.BarCount;
            ImGui.ProgressBar(Math.Clamp(state.CurrentUnits / (float)maximum, 0, 1), new Vector2(0, 0), T(state.IsPvP ? F("Your LB: {0:P0}", Math.Min(state.CurrentUnits / (float)state.BarUnits, 1)) :
                    F("Party LB: {0}/{1} bars ready", state.ReadyBars, state.BarCount)));
            if (state.IsFlashing(seconds)) ImGui.TextDisabled(T(state.Casting ? "Casting your Limit Break — fast flash" : "Your Limit Break used — fast flash"));
        }
        if (limitBreakCapture?.InstantFlashError != null)
            ImGui.TextWrapped(F("Instant-LB flashing unavailable: {0}. Gauge and cast LEDs remain active.", limitBreakCapture.InstantFlashError));
        if (limitBreakCapture?.Errors > 0) ImGui.TextDisabled(F("LB observation errors: {0}", limitBreakCapture.Errors));
    }

    private void DrawHaptics()
    {
        Section("Combat haptics");
        var enabled = configuration.CharacterHaptics;
        if (ImGui.Checkbox(Label("Enable character combat haptics"), ref enabled))
        { configuration.CharacterHaptics = enabled; Changed(); }
        Help("Feel your own skill and weapon sounds through the controller. Native footsteps, mounts and speaker effects stay unchanged. Added feedback pauses when you switch apps. Requires USB and in-game vibration enabled.");
        ImGui.BeginDisabled(!enabled);
        try { DrawCombatHapticSettings(); }
        finally { ImGui.EndDisabled(); }
        Section("Native footsteps");
        var footsteps = configuration.FootstepAmplifier;
        if (ImGui.Checkbox(Label("Amplify native footstep vibration"), ref footsteps))
        { configuration.FootstepAmplifier = footsteps; Changed(); }
        Help("Boost only footsteps the game already sends to vibration. Works separately from combat haptics; pauses while mounted or in the background. Requires USB and in-game vibration enabled.");
        ImGui.BeginDisabled(!footsteps);
        try
        {
            var multiplier = Volatile.Read(ref hapticSettings).FootstepMultiplier * 100;
            if (ImGui.SliderFloat(Label("Footstep vibration"), ref multiplier, 100, HapticSettings.MaximumFootstepMultiplier * 100, "%.0f%%"))
            { configuration.FootstepMultiplier = multiplier / 100; Changed(); }
            Help("100% keeps native strength. 150% adds half as much vibration again. Speaker volume, combat feedback and mount vibration are not amplified.");
        }
        finally { ImGui.EndDisabled(); }
        ImGui.TextDisabled(T("Native footstep strength varies by race."));
        Help("Lalafell may have lighter footsteps, while Roegadyn may feel heavier. The same multiplier can feel different between characters.");
    }

    private void DrawCombatHapticSettings()
    {
        var strength = Volatile.Read(ref hapticSettings).Strength * 100;
        if (ImGui.SliderFloat(Label("Added combat strength"), ref strength, 0, 100, "%.0f%%"))
        { configuration.HapticStrength = strength / 100; Changed(); }
        Help("Strength of the added skill feedback. Native vibration stays unchanged. At 0%, you only feel native effects.");
        Subsection("Spatial feedback");
        var spatial = configuration.SpatialHaptics;
        if (ImGui.Checkbox(Label("Spatial haptics"), ref spatial))
        { configuration.SpatialHaptics = spatial; Changed(); }
        Help("Feel sounds in the left or right grip, using your character's position and the camera's facing. Turn off for regular combat feedback.");
        ImGui.BeginDisabled(!spatial);
        try { DrawSpatialHapticSettings(); }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawSpatialHapticSettings()
    {
        var follow = configuration.HapticFollowImpact;
        if (ImGui.Checkbox(Label("Follow skill impacts"), ref follow))
        { configuration.HapticFollowImpact = follow; Changed(); }
        Help("Gently shift feedback from your character toward your skill's impact. Turn off to keep these sounds centered. Requires Spatial haptics.");
        ImGui.BeginDisabled(!follow);
        try
        {
            var bias = Volatile.Read(ref hapticSettings).ImpactBias * 100;
            if (ImGui.SliderFloat(Label("Impact bias"), ref bias, HapticSettings.MinimumImpactBias * 100, 100, "%.0f%%"))
            { configuration.HapticImpactBias = bias / 100; Changed(); }
            Help("25% is a gentle nudge; higher values follow the impact more strongly. Only direction changes. Enable Follow skill impacts to adjust.");
        }
        finally { ImGui.EndDisabled(); }
        Subsection("Distance");
        var distance = configuration.HapticDistanceFalloff;
        if (ImGui.Checkbox(Label("Distance fade"), ref distance))
        { configuration.HapticDistanceFalloff = distance; Changed(); }
        Help("Nearby sounds feel stronger; distant sounds fade out. Sounds with no known position are skipped. Requires Spatial haptics.");
        ImGui.BeginDisabled(!distance);
        try { DrawHapticDistanceSettings(); }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawHapticDistanceSettings()
    {
        var origin = (int)Volatile.Read(ref hapticSettings).DistanceOrigin;
        if (ImGui.Combo(Label("Distance origin"), ref origin, uiText.Options("Character (recommended)\0Camera\0")))
        { configuration.HapticDistanceSource = (HapticDistanceOrigin)origin; Changed(); }
        var automatic = configuration.HapticAutomaticDistance;
        var snapshot = Volatile.Read(ref hapticSettings);
        var fromCamera = snapshot.DistanceOrigin == HapticDistanceOrigin.Camera;
        Help(fromCamera
            ? "Measure from the camera to the sound. Recommended limits adjust with camera zoom. Custom limits use fixed camera distances."
            : F("Measure from your character to the sound. Camera zoom has no effect. Recommended: full strength to {0:F0} yalms, fading to zero at {1:F0}.", HapticSettings.RecommendedNearDistance, HapticSettings.RecommendedFarDistance));
        if (!ImGui.CollapsingHeader(Label("Advanced distance settings"))) return;
        if (ImGui.Checkbox(Label("Use recommended distance fade"), ref automatic))
        { configuration.HapticAutomaticDistance = automatic; Changed(); }
        Help(fromCamera
            ? F("Use the {0:F0}–{1:F0} yalm range, plus the camera distance from your character. Turn off for custom limits.", HapticSettings.RecommendedNearDistance, HapticSettings.RecommendedFarDistance)
            : F("Full strength to {0:F0} yalms, fading to zero at {1:F0}. Turn off to set your own limits.", HapticSettings.RecommendedNearDistance, HapticSettings.RecommendedFarDistance));
        ImGui.BeginDisabled(automatic);
        try
        {
            snapshot = Volatile.Read(ref hapticSettings);
            var near = snapshot.NearDistance;
            var far = snapshot.FarDistance;
            if (ImGui.SliderFloat(Label("Full strength within (yalms)"), ref near, 0, far - 1, "%.1f"))
            {
                if (fromCamera) configuration.HapticNearDistance = near;
                else configuration.HapticCharacterNearDistance = near;
                Changed();
            }
            Help("Keep full added feedback inside this distance. Turn off recommended limits to adjust.");
            if (ImGui.SliderFloat(Label("Fade to zero at (yalms)"), ref far, near + 1, 100, "%.1f"))
            {
                if (fromCamera) configuration.HapticFarDistance = far;
                else configuration.HapticCharacterFarDistance = far;
                Changed();
            }
            Help("Added feedback reaches zero here. Native vibration is unchanged. Turn off recommended limits to adjust.");
        }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawDiagnostics(bool foreground)
    {
        var capture = Volatile.Read(ref audioCapture);
        ImGui.TextDisabled(F("Version {0}", PluginVersion));
        ImGui.TextUnformatted(T(foreground ? "FFXIV active" : "Background protected — custom output paused"));
        ImGui.TextUnformatted(F("Connected USB controllers: {0}", session.DeviceCount));
        Section("Recording");
        if (sfxTrace != null)
            ImGui.TextUnformatted(F("Recording SFX: {0:F1}s remaining — use your skill now", sfxTrace.Remaining));
        else
        {
            ImGui.BeginDisabled(capture == null);
            try
            {
                if (ImGui.Button(Label("Record SFX diagnostics (15 seconds)"))) StartTrace();
                Help("Choose sound-reactive lighting or enable combat haptics first. Use your skill during the 15-second countdown. Saves sound details, not audio, to sfx-trace.json beside the plugin. Replaces the previous recording.");
            }
            finally { ImGui.EndDisabled(); }
        }
        if (traceStatus != null) ImGui.TextWrapped(F(traceStatus, traceError ?? ""));

        Section("Live status");
        if (ImGui.CollapsingHeader(Label("Lighting output"))) DrawLightingDiagnostics();
        if (ImGui.CollapsingHeader(Label("SFX capture"))) DrawSfxDiagnostics(foreground, capture);
        if (ImGui.CollapsingHeader(Label("Combat haptics"))) DrawHapticDiagnostics(foreground, capture);
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
        var error = Volatile.Read(ref lastError);
        if (error != null) ImGui.TextWrapped(F("Last output error: {0}", error));
    }

    private void DrawSfxDiagnostics(bool foreground, SfxAudioCapture? capture)
    {
        if (audioError != null) ImGui.TextWrapped(F("SFX capture unavailable: {0}", audioError));
        else if (capture?.Faulted == true) ImGui.TextWrapped(T("Sound capture stopped. Reload the plugin to retry."));
        else if (capture == null) ImGui.TextUnformatted(T(audioInitialization == null ? "SFX capture: not started" : "Preparing SFX capture…"));
        else if (!capture.LayoutSupported) ImGui.TextWrapped(T("Waiting for a supported game audio mixer. SFX lighting is paused."));
        else if (settings.AudioSource == SfxSource.PlayerOnly && !capture.PlayerFilterSupported)
            ImGui.TextWrapped(T("Your sounds cannot be identified on this game version. Choose All combat sounds for lighting."));
        else ImGui.TextUnformatted(T(!foreground || !settings.Enabled || !LightingEffects.IsSfx(settings.Effect)
            ? "SFX lighting: paused" : "SFX lighting: ready"));
        ImGui.TextUnformatted(F("SFX RMS: {0:F4}; light level: {1:P0}", Volatile.Read(ref sfxRms), Volatile.Read(ref sfxLevel)));
        if (capture == null) return;
        ImGui.TextUnformatted(F("SFX blocks: {0}; other voices skipped: {1}", capture.MatchedBlocks, capture.RejectedVoices));
        ImGui.TextUnformatted(F("SFX VFX: yours {0}; other {1}; unknown {2}", capture.OwnAnimations, capture.OtherAnimations, capture.UnknownAnimations));
        ImGui.TextUnformatted(F("Your sound reservations: {0}", capture.OwnedReservations));
        ImGui.TextUnformatted(F("Your playback links: {0}", capture.PlaybackLinks));
        if (capture.PlayerFilterError != null) ImGui.TextWrapped(F("Player-only setup: {0}", capture.PlayerFilterError));
    }

    private void DrawHapticDiagnostics(bool foreground, SfxAudioCapture? capture)
    {
        var snapshot = Volatile.Read(ref hapticSettings);
        var enabled = snapshot.Enabled;
        if (capture?.ConfirmationError != null)
            ImGui.TextWrapped(F("Enable confirmation unavailable: {0}", capture.ConfirmationError));
        var output = capture?.Haptics;
        if (!enabled) ImGui.TextUnformatted(T("Added combat haptics: off"));
        else if (audioError != null) ImGui.TextWrapped(F("Haptics unavailable: {0}", audioError));
        else if (capture == null) ImGui.TextUnformatted(T("Preparing character sound detection…"));
        else if (capture.Faulted || output?.Faulted == true) ImGui.TextWrapped(T("Combat feedback stopped. Reload the plugin to retry."));
        else if (!capture.PlayerFilterSupported) ImGui.TextWrapped(T("Your sounds cannot be identified on this game version. Added feedback is paused."));
        else if (capture.HapticsError != null) ImGui.TextWrapped(F("Native haptics unavailable: {0}", capture.HapticsError));
        else if (!capture.LayoutSupported) ImGui.TextWrapped(T("Waiting for a supported game audio mixer."));
        else if (!foreground) ImGui.TextUnformatted(T("Added combat haptics: paused in background"));
        else if (snapshot.Strength <= 0) ImGui.TextUnformatted(T("Added combat strength: 0 — native effects only"));
        else ImGui.TextUnformatted(T("Added combat haptics: ready — use a skill"));
        if (!snapshot.FootstepAmplifier) ImGui.TextUnformatted(T("Native footstep amplifier: off"));
        else if (audioError != null) ImGui.TextWrapped(F("Haptics unavailable: {0}", audioError));
        else if (capture == null) ImGui.TextUnformatted(T("Preparing character sound detection…"));
        else if (capture.HapticsError != null) ImGui.TextWrapped(F("Native haptics unavailable: {0}", capture.HapticsError));
        else if (capture.Faulted || output?.Faulted == true) ImGui.TextWrapped(T("Combat feedback stopped. Reload the plugin to retry."));
        else if (!capture.LayoutSupported) ImGui.TextWrapped(T("Waiting for a supported game audio mixer."));
        else if (!foreground) ImGui.TextUnformatted(T("Native footstep amplifier: paused in background"));
        else if (output?.FootstepContextReady != true) ImGui.TextUnformatted(T("Native footstep amplifier: waiting for an unmounted character"));
        else if (snapshot.FootstepMultiplier <= 1) ImGui.TextUnformatted(T("Footstep vibration: 100% — native strength"));
        else ImGui.TextUnformatted(T("Native footstep amplifier: ready — walk or run"));
        if (snapshot.Spatial)
        {
            if (capture?.SpatialError != null) ImGui.TextWrapped(F("Spatial haptics unavailable: {0}. ", capture.SpatialError) +
                T(configuration.HapticDistanceFalloff ? "Added feedback paused while distance fade is on." : "Using original combat haptics."));
            else if (enabled && output?.ListenerReady != true) ImGui.TextDisabled(T("Waiting for your character and an active gameplay camera."));
            var last = foreground && enabled ? output?.LastSpatial ?? default : default;
            ImGui.ProgressBar(last.Left, new Vector2(0, 0), T("Left grip gain"));
            ImGui.ProgressBar(last.Right, new Vector2(0, 0), T("Right grip gain"));
            var reference = T(Volatile.Read(ref hapticSettings).DistanceOrigin == HapticDistanceOrigin.Camera ? "camera" : "character");
            if (last.Valid) ImGui.TextUnformatted(F("Last sound: {0:F1} yalms from {1}; balance {2:+0.00;-0.00;0.00}", last.Distance, reference, last.Pan));
            if (snapshot.DistanceFalloff)
            {
                var pose = output?.Listener;
                if (snapshot.AutomaticDistance && snapshot.DistanceOrigin == HapticDistanceOrigin.Camera && pose == null)
                    ImGui.TextUnformatted(T("Distance fade: waiting for camera position"));
                else
                {
                    var range = HapticSpatializer.DistanceRange(snapshot, pose);
                    ImGui.TextUnformatted(F("Distance fade ({0}): full at {1:F1}; zero at {2:F1} yalms", reference, range.Near, range.Far));
                }
            }
        }
        ImGui.TextUnformatted(F("Combat blocks added: {0}", output?.MixedBlocks ?? 0));
        ImGui.TextUnformatted(F("Footstep blocks amplified: {0}", output?.FootstepBlocks ?? 0));
        ImGui.TextUnformatted(F("Native vibration blocks preserved: {0}", output?.PreservedNativeBlocks ?? 0));
        ImGui.TextUnformatted(F("Spatial blocks: {0}; original-route fallbacks: {1}", output?.SpatialBlocks ?? 0, output?.UnpositionedBlocks ?? 0));
        ImGui.TextUnformatted(F("Distance cutoff: {0}; missing position/listener: {1}", output?.CutoffBlocks ?? 0, output?.SuppressedBlocks ?? 0));
        ImGui.TextUnformatted(F("Player-origin blocks biased: {0}; impact cue: {1}", output?.BiasedBlocks ?? 0, T(output?.ImpactCue.HasValue == true ? "active" : "none")));
        ImGui.TextUnformatted(T(output?.Available == true ? "Game vibration mixer detected" : "Waiting for the first eligible skill sound"));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        framework.Update -= Update;
        pluginInterface.UiBuilder.Draw -= Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= Open;
        pluginInterface.UiBuilder.OpenMainUi -= Open;
        if (commandRegistered) commands.RemoveHandler(Command);
        limitBreakCapture?.Read(null, 0, false, clock.Elapsed.TotalSeconds, false);
        Volatile.Write(ref limitBreakState, LimitBreakState.Empty);
        Volatile.Read(ref audioCapture)?.SetTracing(false);
        Volatile.Write(ref settings, Volatile.Read(ref settings) with { Enabled = false });
        Volatile.Write(ref hapticSettings, Volatile.Read(ref hapticSettings) with { Enabled = false });
        stop.Cancel();
        worker.GetAwaiter().GetResult();
        // Join even an unfinished setup so a late hook cannot survive plugin unload.
        DisposeInitialization(audioInitialization);
        DisposeInitialization(limitBreakInitialization);
        guard.Dispose();
        stop.Dispose();
        try { if (!double.IsPositiveInfinity(saveAt)) Save(); }
        finally { uiFont.Dispose(); }
        log.Information("PS5 DualSense Controller for FFXIV unloaded; shared device handles closed.");
    }

    private static void DisposeInitialization<T>(Task<T>? initialization) where T : IDisposable
    {
        if (initialization == null) return;
        try { initialization.GetAwaiter().GetResult().Dispose(); }
        catch { /* Failed initializers clean up their hooks; no observer survives unload. */ }
    }
}
