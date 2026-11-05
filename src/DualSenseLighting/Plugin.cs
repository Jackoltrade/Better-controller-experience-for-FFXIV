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
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;
using DualSenseLighting.Ui;

[assembly: System.Reflection.AssemblyVersion("0.18.0.0")]
[assembly: System.Reflection.AssemblyTitle(PluginInfo.Name)]
[assembly: System.Reflection.AssemblyProduct(PluginInfo.Name)]
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows10.0")]

namespace DualSenseLighting;

public sealed partial class Plugin : IDalamudPlugin
{
    private static readonly string PluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    private const string Command = PluginInfo.Command;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly Configuration configuration;
    private readonly UiText uiText = new();
    private readonly IFontHandle uiFont;
    private readonly WindowSystem windowSystem = new("DualSenseLighting");
    private readonly SettingsWindow settingsWindow;
    private readonly DialogWindow dialogWindow;
    private DialogKind dialogKind;
    private enum DialogKind { Info, Recording, DeleteRecordings, Reset }
    private readonly CommandInfo commandInfo;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource stop = new();
    private readonly LightingSession session;
    private readonly GameOutputGuard guard;
    private readonly XInputRumble rumbleDevices;
    private readonly RumbleSession rumble;
    private readonly RumbleSession bluetoothRumble;
    private int selectedHapticOutput = (int)HapticOutputMode.DualSense;
    private long observedBluetoothGeneration;
    private int rumbleTestRequested;
    private bool BluetoothOutput => Volatile.Read(ref selectedHapticOutput) == (int)HapticOutputMode.Bluetooth;
    private bool UsesRegularRumble => BluetoothOutput || rumble.UsesRumble;
    private RumbleSession ActiveRumble => BluetoothOutput ? bluetoothRumble : rumble;
    private string? rumbleError;
    private readonly Task worker;
    private readonly ControllerPreview preview;
    private readonly IGameInteropProvider interop;
    private readonly ISigScanner scanner;
    private readonly IObjectTable objects;
    private readonly IDataManager data;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly EnemyEncounterCatalog enemyEncounters;
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
    private bool setupReady, setupLighting, setupHaptics, showRecordingNotice;
    private double saveAt = double.PositiveInfinity;
    private string? lastError;
    private bool reportedGuardError;
    private SfxTrace? sfxTrace;
    private Task? traceSave;
    private bool traceDeleting;
    private int traceDeleteRequested;
    private readonly Stopwatch traceClock = new();
    private double nextTraceSizeRefresh;
    private long? traceDiskBytes;
    private string TracePath => Path.Combine(pluginInterface.AssemblyLocation.DirectoryName!, "sfx-trace.json");
    private string TraceDirectory => TraceArchive.ArchiveDirectory(TracePath);
    private DateTimeOffset traceStarted;
    private int traceSegment;
    private bool traceStopRequested;
    private int traceToggleRequested;
    private string? traceStatus;
    private string? traceError;
    private bool selectDiagnostics;
    private bool commandRegistered;
    private int disposed;
    private long hapticEnableCueDeadline;
    private GameAudioLayout.EnemyActor[] enemyActors;
    private GameAudioLayout.EnemyActor[] enemyCandidates;
    private readonly BossEncounterState bossEncounter = new();
    private uint enemyTerritory = uint.MaxValue, enemyDuty;
    private byte enemyQueueSize, enemyPartySize, enemyAllianceParties;
    private enum ResetTarget { Lighting, Haptics }
    private ResetTarget? resetTarget;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands,
        IFramework framework, IGameInteropProvider interop, IPluginLog log, ITextureProvider textures, ISigScanner scanner,
        IObjectTable objects, IDataManager data, IClientState clientState, IChatGui chat, ICondition condition)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        this.log = log;
        this.chat = chat;
        this.interop = interop;
        this.scanner = scanner;
        this.objects = objects;
        enemyActors = new GameAudioLayout.EnemyActor[objects.Length];
        enemyCandidates = new GameAudioLayout.EnemyActor[objects.Length];
        this.data = data;
        this.clientState = clientState;
        this.condition = condition;
        enemyEncounters = new EnemyEncounterCatalog(
            data.GetExcelSheet<Lumina.Excel.Sheets.NotoriousMonster>().Select(x => new EnemyHuntDefinition(x.BNpcName.RowId, x.BNpcBase.RowId, x.Rank)),
            data.GetExcelSheet<Lumina.Excel.Sheets.Fate>().Select(x => new EnemyFateDefinition(x.RowId, x.MapIcon, x.Icon)),
            data.GetExcelSheet<Lumina.Excel.Sheets.BNpcBase>().Select(x => new EnemyNpcRankDefinition(x.RowId, x.Rank)));
        if (pluginInterface.InstalledPlugins.Any(p => p.InternalName == "SteamLightingGuard" && p.IsLoaded))
            throw new InvalidOperationException($"Disable the older Steam Lighting Guard before loading {PluginInfo.Name}.");
        var savedConfiguration = pluginInterface.GetPluginConfig() as Configuration;
        configuration = savedConfiguration ?? new Configuration();
        if (savedConfiguration == null) configuration.Enabled = configuration.HapticEnabled = false;
        setupLighting = configuration.Enabled;
        setupHaptics = configuration.HapticEnabled;
        setupReady = configuration.HasCompletedSetup;
        windowOpen = showGeneralInfo = !setupReady;
        dialogWindow = new DialogWindow(DrawDialog);
        settingsWindow = new SettingsWindow(DrawSettings)
        {
            IsOpen = windowOpen,
            Closed = () => dialogWindow.IsOpen = false,
        };
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(dialogWindow);
        uiText.Language = Enum.IsDefined(configuration.Language) ? configuration.Language : UiLanguage.English;
        settings = configuration.Snapshot(requireSetup: true);
        hapticSettings = configuration.HapticsSnapshot(requireSetup: true);
        try
        {
            uiFont = pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
                e.OnPreBuild(tk => tk.AddDalamudDefaultFont(-1, UiText.GlyphRanges)));
            preview = new ControllerPreview(textures, uiText);
            rumbleDevices = new XInputRumble(interop, Native.IsCurrentProcessForeground);
            rumble = new RumbleSession(rumbleDevices, new HidDevices().HasDualSense);
            session = new LightingSession(new HidDevices(), Native.IsCurrentProcessForeground, ReportError);
            bluetoothRumble = new RumbleSession(session, () => false);
            guard = new GameOutputGuard(interop, () => Volatile.Read(ref settings), () => clock.Elapsed.TotalSeconds,
                () => Volatile.Read(ref sfxLevel), () => Volatile.Read(ref limitBreakState),
                setupReady && (configuration.Enabled || configuration.HapticEnabled));
            commandInfo = new CommandInfo(OnCommand) { HelpMessage = T("Configure lighting and haptics. Use /bcef help for feature commands.") };
            commandRegistered = commands.AddHandler(Command, commandInfo);
            if (!commandRegistered) throw new InvalidOperationException(T("The /bcef command is already registered by another plugin."));
            pluginInterface.UiBuilder.Draw += Draw;
            pluginInterface.UiBuilder.OpenConfigUi += Open;
            pluginInterface.UiBuilder.OpenMainUi += Open;
            framework.Update += Update;
            worker = Task.Run(RunWorker);
            log.Information("{PluginName} {Version} enabled. USB/Bluetooth lighting and Bluetooth rumble are foreground-only.", PluginInfo.Name, PluginVersion);
        }
        catch
        {
            if (commandRegistered) commands.RemoveHandler(Command);
            pluginInterface.UiBuilder.Draw -= Draw;
            pluginInterface.UiBuilder.OpenConfigUi -= Open;
            pluginInterface.UiBuilder.OpenMainUi -= Open;
            framework.Update -= Update;
            windowSystem.RemoveAllWindows();
            stop.Cancel();
            worker?.GetAwaiter().GetResult();
            DisposeInitialization(audioInitialization);
            DisposeInitialization(limitBreakInitialization);
            rumble?.Dispose();
            bluetoothRumble?.Dispose();
            rumbleDevices?.Dispose();
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
                if (!Volatile.Read(ref setupReady)) continue;
                var snapshot = Volatile.Read(ref settings);
                var seconds = clock.Elapsed.TotalSeconds;
                var capture = Volatile.Read(ref audioCapture);
                var foreground = Native.IsCurrentProcessForeground();
                var requestedHaptics = Volatile.Read(ref hapticSettings);
                if (!foreground || !requestedHaptics.NeedsAudio || Stopwatch.GetTimestamp() >= Interlocked.Read(ref hapticEnableCueDeadline))
                    Interlocked.Exchange(ref hapticEnableCueDeadline, 0);
                var output = capture?.Haptics;
                session.Discover(snapshot.Enabled, (requestedHaptics.NeedsAudio || Volatile.Read(ref windowOpen)) &&
                    requestedHaptics.OutputMode != HapticOutputMode.XInput, seconds);
                var selected = HapticOutputSelection.Resolve(requestedHaptics.OutputMode, session.HasUsb, session.HasBluetooth);
                if ((int)selected != Volatile.Read(ref selectedHapticOutput) || observedBluetoothGeneration != session.BluetoothGeneration)
                {
                    rumble.Release(); bluetoothRumble.Release();
                    output?.ResetRumbleSignals();
                }
                observedBluetoothGeneration = session.BluetoothGeneration;
                Volatile.Write(ref selectedHapticOutput, (int)selected);
                var previousRumbleSlot = ActiveRumble.Slot;
                try
                {
                    if (BluetoothOutput)
                    {
                        rumble.Release();
                        bluetoothRumble.Select(HapticOutputMode.XInput, 0, foreground && requestedHaptics.NeedsAudio, seconds);
                        Volatile.Write(ref rumbleError, null);
                    }
                    else
                    {
                        bluetoothRumble.Release();
                        if (rumble.Select(selected == HapticOutputMode.XInput ? requestedHaptics.OutputMode : HapticOutputMode.DualSense,
                            requestedHaptics.RumbleController, foreground && requestedHaptics.NeedsAudio, seconds))
                            Volatile.Write(ref rumbleError, null);
                    }
                }
                catch (Exception error) { rumble.Release(); bluetoothRumble.Release(); Volatile.Write(ref rumbleError, error.Message); }
                if (previousRumbleSlot != ActiveRumble.Slot) output?.ResetRumbleSignals();
                output?.Configure(requestedHaptics, foreground,
                    capture!.PlayerFilterSupported, capture.LayoutSupported && !capture.Faulted &&
                    (UsesRegularRumble ? ActiveRumble.Slot >= 0 && rumbleError == null : capture.HapticsError == null),
                    UsesRegularRumble, BluetoothOutput,
                    nativeFootstepSupported: HapticOutputSelection.SupportsFootstepAmplification(
                        requestedHaptics.OutputMode, session.HasUsb, session.HasBluetooth));
                var confirm = output?.CurrentSettings.NeedsAudio == true &&
                    Interlocked.Exchange(ref hapticEnableCueDeadline, 0) > Stopwatch.GetTimestamp();
                if (UsesRegularRumble && confirm) ActiveRumble.RequestConfirmation(seconds);
                capture?.UpdateEnableCue(!UsesRegularRumble && confirm);
                var testRequest = Interlocked.Exchange(ref rumbleTestRequested, 0);
                if (testRequest != 0 && UsesRegularRumble && foreground &&
                    output?.CurrentSettings.NeedsAudio == true)
                {
                    ActiveRumble.RequestStrengthTest(seconds, testRequest == 2);
                    if (BluetoothOutput) session.StartStrengthTest(seconds);
                }
                if (output != null)
                    ActiveRumble.Tick(output.RumbleSignal, output.CurrentSettings,
                        foreground && output.CurrentSettings.NeedsAudio && !output.Faulted && capture?.Faulted == false, seconds,
                        output.ScreenEffectLevel(Stopwatch.GetTimestamp()), BluetoothOutput
                            ? (output.RumbleLeft.TakePeak(), output.RumbleRight.TakePeak()) : null);
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
            rumble.Release();
            bluetoothRumble.Release();
            Volatile.Write(ref sfxLevel, 0);
            await session.ReleaseAsync(true, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void ReportError(Exception error)
    {
        var message = error.Message;
        if (Interlocked.Exchange(ref lastError, message) != message)
            log.Warning(error, "DualSense output paused; reconnecting devices will be retried.");
    }

    private void OnCommand(string command, string args)
    {
        var parsed = PluginCommand.Parse(args);
        switch (parsed.Kind)
        {
            case PluginCommandKind.Open: Open(); return;
            case PluginCommandKind.Info: showGeneralInfo = true; Open(); return;
            case PluginCommandKind.Trace: RequestTraceToggle(); Open(); return;
            case PluginCommandKind.Help: PrintCommandHelp(); return;
            case PluginCommandKind.Invalid:
                chat.PrintError(PluginInfo.ChatPrefix + T("Unknown command or value. Use /bcef help for syntax."));
                return;
            case PluginCommandKind.Switch: break;
            default: return;
        }
        if (!configuration.HasCompletedSetup)
        {
            chat.Print(PluginInfo.ChatPrefix + T("Choose your controller effects in Info before using feature commands."));
            showGeneralInfo = true;
            Open();
            return;
        }
        if (configuration.SetFeature(parsed.Feature, parsed.Switch)) Changed();
        var enabled = configuration.IsFeatureEnabled(parsed.Feature);
        var label = parsed.Feature switch
        {
            ControllerFeature.Lighting => "Custom lighting",
            ControllerFeature.Haptics => "Enable haptics",
            ControllerFeature.CombatHaptics => "Combat haptics",
            ControllerFeature.PlayerSfx => "Player SFX based",
            ControllerFeature.Footsteps => "Amplify native footstep vibration",
            ControllerFeature.SpatialHaptics => "Spatial haptics",
            ControllerFeature.DistanceFade => "Distance fade",
            _ => "Follow skill impacts",
        };
        chat.Print(PluginInfo.ChatPrefix + F("{0}: {1}", T(label), T(enabled ? "On" : "Off")));
        if (enabled && parsed.Feature is not (ControllerFeature.Lighting or ControllerFeature.Haptics))
        {
            if (!configuration.HapticEnabled)
                chat.Print(PluginInfo.ChatPrefix + T("Saved. Enable haptics to use this setting."));
            else if (parsed.Feature == ControllerFeature.Footsteps &&
                !HapticOutputSelection.SupportsFootstepAmplification(configuration.HapticOutput, session.HasUsb, session.HasBluetooth))
                chat.Print(PluginInfo.ChatPrefix + T("Connect a DualSense or DualSense Edge by USB and select Automatic or DualSense haptics."));
            else if (parsed.Feature is not (ControllerFeature.CombatHaptics or ControllerFeature.Footsteps) && !configuration.CombatHaptics)
                chat.Print(PluginInfo.ChatPrefix + T("Saved. Enable combat haptics to use this setting."));
            else if (parsed.Feature == ControllerFeature.FollowImpact)
            {
                if (!configuration.CharacterHaptics)
                    chat.Print(PluginInfo.ChatPrefix + T("Saved. Enable Player SFX to use this setting."));
                else if (!configuration.SpatialHaptics)
                    chat.Print(PluginInfo.ChatPrefix + T("Saved. Enable spatial haptics to use this setting."));
            }
        }
    }

    private void PrintCommandHelp()
    {
        chat.Print(PluginInfo.ChatPrefix + T("/bcef opens settings. /bcef info opens the guide. /bcef trace starts or stops diagnostics."));
        chat.Print(T("Usage: /bcef <feature> <on|off|toggle>"));
        chat.Print(T("Features: lighting, haptics, combathaptics, playersfx, footsteps, spatialhaptics, distancefade, followimpact."));
    }

    private void RequestTraceToggle()
    {
        selectDiagnostics = true;
        if (sfxTrace == null && !configuration.CanRecordDiagnostics())
        {
            if (!configuration.HasCompletedSetup) showGeneralInfo = true;
            else showRecordingNotice = true;
            Open();
            return;
        }
        Interlocked.Increment(ref traceToggleRequested);
    }

    // UI and commands only post intent. The framework owns the recorder, final
    // native snapshot, and save queue, so a segment cannot mutate while saving.
    private void ToggleTrace()
    {
        selectDiagnostics = true;
        if (sfxTrace != null) { traceStopRequested = true; return; }
        if (!configuration.CanRecordDiagnostics()) { RequestTraceToggle(); return; }
        CompleteTraceSave();
        if (traceSave != null) return;
        traceError = null;
        var capture = Volatile.Read(ref audioCapture);
        if (capture == null) { traceStatus = "Select an SFX effect or enable haptics, then wait for capture to be ready."; return; }
        traceStarted = DateTimeOffset.UtcNow;
        traceClock.Restart();
        nextTraceSizeRefresh = 0;
        traceDiskBytes = null;
        traceSegment = 1;
        sfxTrace = new SfxTrace(PluginVersion, traceStarted, traceSegment);
        capture.SetTracing(true);
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

    private void StopTrace()
    {
        var recording = sfxTrace;
        if (recording == null) return;
        traceStopRequested = false;
        sfxTrace = null;
        traceClock.Stop();
        Volatile.Read(ref audioCapture)?.SetTracing(false);
        QueueTraceSave(recording);
        traceStatus = "Saving recording...";
    }

    private void CompleteTraceSave()
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
    private void Open()
    {
        settingsWindow.IsOpen = true;
        if (dialogWindow.IsOpen) dialogWindow.BringToFront();
        else settingsWindow.BringToFront();
        Volatile.Write(ref windowOpen, true);
        if (!configuration.HasCompletedSetup) showGeneralInfo = true;
    }

    private unsafe void Update(IFramework _)
    {
        CompleteTraceSave();
        if (Interlocked.Exchange(ref traceDeleteRequested, 0) != 0) DeleteRecordings();
        if ((Interlocked.Exchange(ref traceToggleRequested, 0) & 1) != 0) ToggleTrace();
        if (!Volatile.Read(ref setupReady))
        {
            if (clock.Elapsed.TotalSeconds >= saveAt) Save();
            return;
        }
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
            audioInitialization = Task.Run(() => new SfxAudioCapture(interop, scanner, data));
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
            UpdateEnemyActors(capture, address != 0);
            capture.UpdateScreenEffects(address != 0 && !clientState.IsPvP && !condition[ConditionFlag.BetweenAreas] &&
                !condition[ConditionFlag.BetweenAreas51] && Native.IsCurrentProcessForeground(), clientState.TerritoryType, address);
            if (sfxTrace != null)
            {
                try
                {
                    var segmentComplete = sfxTrace.Sample(capture, Volatile.Read(ref hapticSettings), objects, traceStopRequested);
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
        else UpdateEnemyActors(null, (objects.LocalPlayer?.Address ?? 0) != 0);
        if (clock.Elapsed.TotalSeconds >= saveAt) Save();
        if (!reportedGuardError && guard.Errors > 0)
        {
            reportedGuardError = true;
            log.Warning("A controller output could not be inspected. Original game output was passed through.");
        }
    }

    private unsafe void UpdateEnemyActors(SfxAudioCapture? capture, bool playerPresent)
    {
        // Keep the full object-table snapshot, including every ownership-chain
        // ancestor and player seed, if Dalamud expands its table after startup.
        if (enemyCandidates.Length < objects.Length)
        {
            Array.Resize(ref enemyCandidates, objects.Length);
            Array.Resize(ref enemyActors, objects.Length);
        }
        var territory = clientState.TerritoryType;
        var haptics = Volatile.Read(ref hapticSettings);
        var allowed = playerPresent && !clientState.IsPvP && !condition[ConditionFlag.BetweenAreas] &&
            !condition[ConditionFlag.BetweenAreas51] && Native.IsCurrentProcessForeground() &&
            (haptics.EnemiesEnabled || haptics.HitReactions);
        if (enemyTerritory != territory)
        {
            enemyTerritory = territory;
            enemyDuty = data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                .GetRowOrDefault(territory)?.ContentFinderCondition.RowId ?? 0;
            var duty = data.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>().GetRowOrDefault(enemyDuty);
            var members = duty?.ContentMemberType.ValueNullable;
            enemyQueueSize = duty?.QueueMaxPlayers ?? 0;
            enemyPartySize = members?.MembersPerParty ?? 0;
            enemyAllianceParties = members?.AlliancePartyCount ?? 0;
        }
        var localPlayer = objects.LocalPlayer;
        var playerInCombat = localPlayer != null && (localPlayer.StatusFlags & StatusFlags.InCombat) != 0;
        if (enemyDuty == 0 && (capture == null || !allowed && sfxTrace == null))
        {
            bossEncounter.Update(territory, 0, localPlayer?.EntityId ?? 0, false, false);
            capture?.SetEnemies(ReadOnlySpan<GameAudioLayout.EnemyActor>.Empty, territory, false);
            capture?.UpdateArena(territory, false);
            return;
        }
        // Seeds include every player and player-like NPC. Ownership chains are
        // excluded before registering any source, including when audio is paused.
        Span<uint> playerSources = stackalloc uint[objects.Length];
        var playerSourceCount = 0;
        var count = 0;
        var bossInCombat = false;
        if (playerPresent && !clientState.IsPvP)
            for (var index = 0; index < objects.Length; index++)
            {
                if (objects[index] is not ICharacter actor ||
                    actor.Address == 0 || actor.EntityId is 0 or 0xE0000000) continue;
                if (actor is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter || actor.ObjectKind == ObjectKind.BattleNpc &&
                    actor.SubKind is 2 or 3 or 4 or 9)
                { playerSources[playerSourceCount++] = actor.EntityId; continue; }
                if (actor.ObjectKind != ObjectKind.BattleNpc || actor.SubKind is not (1 or 5 or 11)) continue;
                var battleActor = actor.SubKind == (byte)BattleNpcSubKind.Combatant || actor.SubKind == (byte)BattleNpcSubKind.BNpcPart;
                var combatant = battleActor && (actor.StatusFlags & StatusFlags.Hostile) != 0;
                // FateId is the pinned GameObject field at +0xF8. Membership
                // and NPC rank are both required; nearby FATE trash is not a boss.
                var boss = enemyEncounters.IsBoss(enemyDuty, actor.NameId, actor.BaseId, *(ushort*)(actor.Address + 0xF8));
                // The shared roster selector requires a live boss with the same
                // NPC name. This also supports helpers for open-world bosses.
                var helper = actor.SubKind == GameAudioLayout.BossMechanicSubKind;
                var owner = *(uint*)(actor.Address + 0x88);
                var bossOwnerName = battleActor ? BossMechanics.OwnerName(enemyDuty, actor.NameId, actor.BaseId) : 0;
                var summon = battleActor && (BossMechanics.HasOwner(owner) || bossOwnerName != 0);
                var eventId = *(uint*)(actor.Address + 0xF4);
                var layoutId = *(uint*)(actor.Address + 0x7C);
                enemyCandidates[count++] = new(actor.Address, actor.EntityId, *(nint*)(actor.Address + 0x100), actor.ObjectIndex,
                    combatant && !boss ? EnemySoundKind.Regular : EnemySoundKind.Boss,
                    combatant ? EnemyActorRole.Combatant : helper ? EnemyActorRole.BossMechanic :
                        summon ? EnemyActorRole.BossSummon : EnemyActorRole.Unclassified,
                    actor.NameId, actor.BaseId, owner, bossOwnerName, eventId, layoutId);
            }
        // Exclusion keeps candidate order; active roots are matched by identity
        // after filtering so a player-owned NPC cannot activate boss routing.
        count = bossEncounter.ExcludePlayerOwned(enemyCandidates.AsSpan(0, count), playerSources[..playerSourceCount]);
        for (var i = 0; i < count; i++)
        {
            var source = enemyCandidates[i];
            if (source.ObjectIndex < 0 || source.ObjectIndex >= objects.Length) continue;
            if (source.Kind == EnemySoundKind.Boss && source.Role == EnemyActorRole.Combatant &&
                objects[source.ObjectIndex] is ICharacter actor && actor.Address == source.Actor &&
                actor.EntityId == source.EntityId && (actor.StatusFlags & StatusFlags.InCombat) != 0)
                bossInCombat = true;
        }
        bossEncounter.Update(territory, clientState.IsPvP ? 0 : enemyDuty,
            localPlayer?.EntityId ?? 0, playerInCombat, bossInCombat);
        // Only independently classified hostile enemies enter regular history.
        // Neutral/preloaded boss performers are never marked as trash.
        var selected = GameAudioLayout.SelectEnemyActors(enemyCandidates.AsSpan(0, count), enemyActors);
        for (var i = 0; i < selected; i++)
            if (enemyActors[i].Kind == EnemySoundKind.Regular) bossEncounter.RememberRegular(enemyActors[i]);
        if (bossEncounter.Active)
        {
            for (var i = 0; i < count; i++)
                enemyCandidates[i] = bossEncounter.Classify(enemyCandidates[i]);
            selected = GameAudioLayout.SelectEnemyActors(enemyCandidates.AsSpan(0, count), enemyActors);
        }
        capture?.SetEnemies(enemyActors.AsSpan(0, selected), territory, allowed,
            new(enemyDuty, enemyQueueSize, enemyPartySize, enemyAllianceParties, playerInCombat, bossInCombat,
                bossEncounter.Active, bossEncounter.Count, bossEncounter.Saturated));
        // Publish the roster first so a newly discovered actor retains this
        // cast on its existing identity without changing roster equality.
        for (var i = 0; i < selected; i++)
        {
            var source = enemyActors[i];
            if (source.ObjectIndex < 0 || source.ObjectIndex >= objects.Length) continue;
            if (objects[source.ObjectIndex] is IBattleChara casting && casting.Address == source.Actor &&
                casting.EntityId == source.EntityId && casting.IsCasting && casting.CastActionType == 1)
                capture?.ObserveEnemyCast(source.Actor, source.EntityId, casting.CastActionId, casting.CastActionType);
        }
        capture?.UpdateArena(territory, allowed && bossEncounter.Active &&
            (haptics.BossEnabled || sfxTrace != null),
            prepare: playerPresent && !clientState.IsPvP && enemyDuty != 0 && (haptics.BossEnabled || sfxTrace != null));
    }

    private void Changed()
    {
        guard.SetEnabled(configuration.HasCompletedSetup && (configuration.Enabled || configuration.HapticEnabled));
        Volatile.Write(ref settings, configuration.Snapshot(requireSetup: true));
        var previous = Volatile.Read(ref hapticSettings);
        var current = configuration.HapticsSnapshot(requireSetup: true);
        Volatile.Write(ref hapticSettings, current);
        Volatile.Write(ref setupReady, configuration.HasCompletedSetup);
        if (!previous.CombatEnabled && current.CombatEnabled || !previous.NeedsAudio && current.NeedsAudio)
            Interlocked.Exchange(ref hapticEnableCueDeadline, Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2);
        else if (!current.NeedsAudio) Interlocked.Exchange(ref hapticEnableCueDeadline, 0);
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
        // Include the title bar and PreDraw constraints in the language font.
        using var font = uiFont.Push();
        try { windowSystem.Draw(); }
        finally { Volatile.Write(ref windowOpen, settingsWindow.IsOpen); }
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
            commandInfo.HelpMessage = T("Configure lighting and haptics. Use /bcef help for feature commands.");
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
            var level = foreground && settings.Enabled ? Volatile.Read(ref sfxLevel) : 0;
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

    private void RefreshTraceSize()
    {
        var seconds = clock.Elapsed.TotalSeconds;
        if (seconds < nextTraceSizeRefresh) return;
        nextTraceSizeRefresh = seconds + 1;
        try
        {
            traceDiskBytes = TraceArchive.RecordingFiles(TracePath)
                .Concat(TraceArchive.RecordingFiles(TraceArchive.ScreenTimingPath))
                .Distinct(StringComparer.OrdinalIgnoreCase).Sum(path => new FileInfo(path).Length);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Saving, retention or manual cleanup can race this display-only check.
            traceDiskBytes = null;
        }
    }

    private void DrawDiagnostics(bool foreground)
    {
        var capture = Volatile.Read(ref audioCapture);
        ImGui.TextDisabled(F("Version {0}", PluginVersion));
        ImGui.TextUnformatted(T(foreground ? "FFXIV active" : "Background protected — custom output paused"));
        ImGui.TextUnformatted(F("Connected controllers: USB {0}; Bluetooth {1}", session.UsbCount, session.BluetoothCount));
        Section("Recording");
        DrawRecordingStorage();
        if (sfxTrace != null)
        {
            ImGui.TextUnformatted(F("Recording SFX: {0:F0}s elapsed", traceClock.Elapsed.TotalSeconds));
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
        if (traceStatus != null) ImGui.TextWrapped(F(traceStatus, traceError ?? ""));

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
        ImGui.TextUnformatted(T(configuration.HapticEnabled ? "Haptics: enabled" : "Haptics: off — your choices are kept"));
        if (!UsesRegularRumble && capture?.ConfirmationError != null)
            ImGui.TextWrapped(F("Enable confirmation unavailable: {0}", capture.ConfirmationError));
        var output = capture?.Haptics;
        if (rumbleError != null) ImGui.TextWrapped(F("Regular rumble unavailable: {0}", rumbleError));
        else if (BluetoothOutput)
        {
            ImGui.TextUnformatted(T(ActiveRumble.Slot >= 0 ? "Bluetooth rumble: ready" : "Bluetooth rumble: waiting for a DualSense"));
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
        else if (UsesRegularRumble)
            ImGui.TextUnformatted(ActiveRumble.Slot >= 0 ? F("Regular rumble: controller {0}", ActiveRumble.Slot + 1) : T("Regular rumble: waiting for an XInput controller"));
        else ImGui.TextUnformatted(T("Vibration output: native DualSense haptics"));
        if (!enabled) ImGui.TextUnformatted(T("Player SFX: off"));
        else if (audioError != null) ImGui.TextWrapped(F("Haptics unavailable: {0}", audioError));
        else if (capture == null) ImGui.TextUnformatted(T("Preparing character sound detection…"));
        else if (capture.Faulted || output?.Faulted == true) ImGui.TextWrapped(T("Combat feedback stopped. Reload the plugin to retry."));
        else if (!capture.PlayerFilterSupported) ImGui.TextWrapped(T("Your sounds cannot be identified on this game version. Added feedback is paused."));
        else if (!UsesRegularRumble && capture.HapticsError != null) ImGui.TextWrapped(F("Native haptics unavailable: {0}", capture.HapticsError));
        else if (!capture.LayoutSupported) ImGui.TextWrapped(T("Waiting for a supported game audio mixer."));
        else if (!foreground) ImGui.TextUnformatted(T("Player SFX: paused in background"));
        else if (UsesRegularRumble && (ActiveRumble.Slot < 0 || rumbleError != null))
            ImGui.TextUnformatted(T("Player SFX: waiting for a rumble controller"));
        else if (snapshot.Strength <= 0) ImGui.TextUnformatted(T("Player SFX strength: 0 — native effects only"));
        else ImGui.TextUnformatted(T("Player SFX: ready — use a skill"));
        if (!snapshot.FootstepAmplifier) ImGui.TextUnformatted(T("Native footstep amplifier: off"));
        else if (!HapticOutputSelection.SupportsFootstepAmplification(snapshot.OutputMode, session.HasUsb, session.HasBluetooth))
            ImGui.TextWrapped(T("Connect a DualSense or DualSense Edge by USB and select Automatic or DualSense haptics."));
        else if (audioError != null) ImGui.TextWrapped(F("Haptics unavailable: {0}", audioError));
        else if (capture == null) ImGui.TextUnformatted(T("Preparing character sound detection…"));
        else if (!UsesRegularRumble && capture.HapticsError != null) ImGui.TextWrapped(F("Native haptics unavailable: {0}", capture.HapticsError));
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
            if (!UsesRegularRumble || BluetoothOutput)
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
            if (!UsesRegularRumble && capture?.ConfirmationError != null)
                ImGui.TextWrapped(F("Screen effect output unavailable: {0}", capture.ConfirmationError));
            ImGui.TextUnformatted(F("Screen effect level: {0:P0}", output?.ScreenEffectLevel(Stopwatch.GetTimestamp()) ?? 0));
            ImGui.TextUnformatted(F("Screen effect blocks: {0}; suppressed: {1}", output?.ScreenBlocks ?? 0, output?.ScreenSuppressedBlocks ?? 0));
        }
        ImGui.TextUnformatted(F("Enemy blocks: bosses {0}; regular enemies {1}", output?.BossBlocks ?? 0, output?.RegularEnemyBlocks ?? 0));
        ImGui.TextUnformatted(F("Boss SFX rule: {0}; remembered regular enemies: {1}",
            T(capture?.EnemyEncounter.BossSfx == true ? "active" : "inactive"), bossEncounter.Count));
        if (capture?.EnemySkillError != null) ImGui.TextWrapped(F("Enemy skill detection unavailable: {0}", capture.EnemySkillError));
        if (capture?.ArenaSoundError != null) ImGui.TextWrapped(F("Ground-effect audio unavailable: {0}", capture.ArenaSoundError));
        if (snapshot.BossEnabled) ImGui.TextUnformatted(F("Active ground-effect sources: {0}", capture?.ArenaSources.Length ?? 0));
        ImGui.TextUnformatted(F("Enemy actions: skills {0}; basic attacks {1}; unknown {2}",
            capture?.EnemySkillActions ?? 0, capture?.EnemyAutoAttacks ?? 0, capture?.UnknownEnemyActions ?? 0));
        ImGui.TextUnformatted(T(!snapshot.EnemiesEnabled ? "Enemy haptics: off" : capture?.EnemySkillError != null && capture?.EnemyEncounter.BossSfx != true
            ? "Enemy haptics: detection unavailable" : clientState.IsPvP
            ? "Enemy haptics: paused in PvP" : !foreground ? "Enemy haptics: paused in background" :
            output?.EnemiesActive == true ? "Enemy haptics: active" : "Enemy haptics: waiting for sound detection"));
        ImGui.TextUnformatted(F("Footstep blocks amplified: {0}", output?.FootstepBlocks ?? 0));
        ImGui.TextUnformatted(F("Native vibration blocks preserved: {0}", output?.PreservedNativeBlocks ?? 0));
        ImGui.TextUnformatted(F("Spatial blocks: {0}; original-route fallbacks: {1}", output?.SpatialBlocks ?? 0, output?.UnpositionedBlocks ?? 0));
        ImGui.TextUnformatted(F("Distance cutoff: {0}; missing position/listener: {1}", output?.CutoffBlocks ?? 0, output?.SuppressedBlocks ?? 0));
        ImGui.TextUnformatted(F("Player-origin blocks biased: {0}; impact cue: {1}", output?.BiasedBlocks ?? 0, T(output?.ImpactCue.HasValue == true ? "active" : "none")));
        if (!UsesRegularRumble)
            ImGui.TextUnformatted(T(output?.Available == true ? "Game vibration mixer detected" : "Waiting for the first eligible skill sound"));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        framework.Update -= Update;
        pluginInterface.UiBuilder.Draw -= Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= Open;
        pluginInterface.UiBuilder.OpenMainUi -= Open;
        windowSystem.RemoveAllWindows();
        Volatile.Write(ref windowOpen, false);
        if (commandRegistered) commands.RemoveHandler(Command);
        limitBreakCapture?.Read(null, 0, false, clock.Elapsed.TotalSeconds, false);
        Volatile.Write(ref limitBreakState, LimitBreakState.Empty);
        StopTrace();
        Volatile.Read(ref audioCapture)?.SetTracing(false);
        Volatile.Write(ref settings, Volatile.Read(ref settings) with { Enabled = false });
        Volatile.Write(ref hapticSettings, Volatile.Read(ref hapticSettings) with
            { Enabled = false, BossEnabled = false, RegularEnemyEnabled = false, FootstepAmplifier = false, HitReactions = false });
        stop.Cancel();
        worker.GetAwaiter().GetResult();
        // Join even an unfinished setup so a late hook cannot survive plugin unload.
        DisposeInitialization(audioInitialization);
        DisposeInitialization(limitBreakInitialization);
        try { traceSave?.GetAwaiter().GetResult(); }
        catch (Exception error) { log.Warning(error, "SFX diagnostic recording could not be saved on unload."); }
        rumble.Dispose();
        bluetoothRumble.Dispose();
        rumbleDevices.Dispose();
        guard.Dispose();
        stop.Dispose();
        try { if (!double.IsPositiveInfinity(saveAt)) Save(); }
        finally { uiFont.Dispose(); }
        log.Information("{PluginName} unloaded; shared device handles closed.", PluginInfo.Name);
    }

    private static void DisposeInitialization<T>(Task<T>? initialization) where T : IDisposable
    {
        if (initialization == null) return;
        try { initialization.GetAwaiter().GetResult().Dispose(); }
        catch { /* Failed initializers clean up their hooks; no observer survives unload. */ }
    }
}
