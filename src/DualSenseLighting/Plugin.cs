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

[assembly: System.Reflection.AssemblyVersion("0.20.0.0")]
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
    private readonly IKeyState keyState;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly Configuration configuration;
    private readonly UiText uiText = new();
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
    private bool setupReady;
    private readonly SettingsUi ui;
    private readonly ConfigurationSaveScheduler saves;
    private string? lastError;
    private bool reportedGuardError;
    private bool reportedEnemyRelationError;
#if BCEF_DIAGNOSTICS
    private readonly DiagnosticRecordingCoordinator recording;
#endif
    private bool commandRegistered;
    private int disposed;
#if BCEF_DIAGNOSTICS
    private int measureUpdates;
    private readonly UpdateTiming updateTiming = new();
#endif
    private long hapticEnableCueDeadline;
    private GameAudioLayout.EnemyActor[] enemyActors;
    private GameAudioLayout.EnemyActor[] enemyCandidates;
    private readonly BossEncounterState bossEncounter = new();
    private uint enemyTerritory = uint.MaxValue, enemyDuty;
    private byte enemyQueueSize, enemyPartySize, enemyAllianceParties;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands,
        IFramework framework, IGameInteropProvider interop, IPluginLog log, ITextureProvider textures, ISigScanner scanner,
        IObjectTable objects, IDataManager data, IClientState clientState, IChatGui chat, ICondition condition, IKeyState keyState)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        this.keyState = keyState;
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
        saves = new ConfigurationSaveScheduler(() => pluginInterface.SavePluginConfig(configuration),
            error => log.Warning(error, "Configuration could not be saved; retrying in five seconds."));
#if BCEF_DIAGNOSTICS
        recording = new DiagnosticRecordingCoordinator(pluginInterface.AssemblyLocation.DirectoryName!, TraceArchive.ScreenTimingPath, configuration, log,
            () => Volatile.Read(ref audioCapture), PluginVersion, clock);
#endif
        setupReady = configuration.HasCompletedSetup;
        settings = configuration.Snapshot(requireSetup: true);
        hapticSettings = configuration.HapticsSnapshot(requireSetup: true);
        try
        {
            rumbleDevices = new XInputRumble(interop, Native.IsCurrentProcessForeground);
            rumble = new RumbleSession(rumbleDevices, new HidDevices().HasDualSense);
            session = new LightingSession(new HidDevices(), Native.IsCurrentProcessForeground, ReportError);
            bluetoothRumble = new RumbleSession(session, () => false);
            guard = new GameOutputGuard(interop, () => Volatile.Read(ref settings), () => clock.Elapsed.TotalSeconds,
                () => Volatile.Read(ref sfxLevel), () => Volatile.Read(ref limitBreakState),
                setupReady && (configuration.Enabled || configuration.HapticEnabled));
            ui = new SettingsUi(configuration, uiText, pluginInterface, textures, session, guard,
#if BCEF_DIAGNOSTICS
                recording,
#endif
                saves, clock,
                () => Volatile.Read(ref settings), () => Volatile.Read(ref hapticSettings),
                () => new UiRuntimeState(Volatile.Read(ref audioCapture), limitBreakCapture, audioError, limitBreakError,
                    rumbleError, Volatile.Read(ref lastError), Volatile.Read(ref sfxLevel), Volatile.Read(ref sfxRms),
                    BluetoothOutput, UsesRegularRumble, ActiveRumble.Slot, bossEncounter.Count, Volatile.Read(ref limitBreakState), clientState.IsPvP,
#if BCEF_DIAGNOSTICS
                    Volatile.Read(ref measureUpdates) != 0, updateTiming,
#endif
                    audioInitialization != null),
                Changed, PublishSettings, Save, request => Interlocked.Exchange(ref rumbleTestRequested, request),
                () => { if (commandInfo != null) commandInfo.HelpMessage = uiText.Get("Configure lighting and haptics. Use /bcef help for feature commands."); }, ReportError
#if BCEF_DIAGNOSTICS
                , enabled => Volatile.Write(ref measureUpdates, enabled ? 1 : 0)
#endif
                );
            windowOpen = ui.IsOpen;
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
            // Preserve the load failure while still attempting every acquired owner.
            Cleanup("initialization", Shutdown);
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
                            ? output.RumbleStereo.TakePeak() : null);
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
            Cleanup("audio loudness", () => Volatile.Read(ref audioCapture)?.Loudness.SetActive(false));
            Cleanup("haptic output", () => Volatile.Read(ref audioCapture)?.Haptics?.Configure(new HapticSettings(), false, false));
            Cleanup("enable cue", () => Volatile.Read(ref audioCapture)?.UpdateEnableCue(false));
            Cleanup("XInput release", () => rumble.Release());
            Cleanup("Bluetooth release", () => bluetoothRumble.Release());
            Volatile.Write(ref sfxLevel, 0);
            try { await session.ReleaseAsync(true, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) { LogCleanupError("controller release", error); }
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
        if (Volatile.Read(ref disposed) != 0) return;
        var parsed = PluginCommand.Parse(args);
        switch (parsed.Kind)
        {
            case PluginCommandKind.Open: Open(); return;
            case PluginCommandKind.Info: ui.ShowInfo(); Open(); return;
#if BCEF_DIAGNOSTICS
            case PluginCommandKind.Trace:
                if (ui.RequestTraceToggle()) Open();
                else chat.Print(PluginInfo.ChatPrefix + T("Diagnostics is only available for development installs."));
                return;
#endif
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
            ui.ShowInfo();
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
#if BCEF_DIAGNOSTICS
        chat.Print(PluginInfo.ChatPrefix + T(ui.DiagnosticsAvailable
            ? "/bcef opens settings. /bcef info opens the guide. /bcef trace starts or stops diagnostics."
            : "/bcef opens settings. /bcef info opens the guide."));
#else
        chat.Print(PluginInfo.ChatPrefix + T("/bcef opens settings. /bcef info opens the guide."));
#endif
        chat.Print(T("Usage: /bcef <feature> <on|off|toggle>"));
        chat.Print(T("Features: lighting, haptics, combathaptics, playersfx, footsteps, spatialhaptics, distancefade, followimpact."));
    }

    private void Open()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        ui.Open();
        Volatile.Write(ref windowOpen, true);
    }
    private void Draw()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        try { ui.Draw(); }
        finally { Volatile.Write(ref windowOpen, ui.IsOpen); }
    }
    private string T(string english) => uiText.Get(english);
    private string F(string english, params object[] values) => uiText.Format(english, values);

    private void Update(IFramework framework)
    {
        if (Volatile.Read(ref disposed) != 0) return;
#if BCEF_DIAGNOSTICS
        var timing = updateTiming.Begin(Volatile.Read(ref measureUpdates) != 0);
        try { UpdateCore(framework, ref timing); }
        finally { timing.Complete(); }
#else
        UpdateCore(framework);
#endif
    }

    private unsafe void UpdateCore(IFramework _
#if BCEF_DIAGNOSTICS
        , ref UpdateTiming.Frame timing
#endif
        )
    {
        ui.ProcessInput(keyState);
#if BCEF_DIAGNOSTICS
        recording.Update();
#endif
        if (!Volatile.Read(ref setupReady))
        {
#if BCEF_DIAGNOSTICS
            timing.Next(UpdateTiming.Phase.SaveGuard);
#endif
            saves.TrySaveDue(clock.Elapsed.TotalSeconds);
            return;
        }
#if BCEF_DIAGNOSTICS
        timing.Next(UpdateTiming.Phase.Observers);
#endif
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
            audioInitialization = Task.Run(() => new SfxAudioCapture(interop, scanner, data, ActionTargetPosition
#if BCEF_DIAGNOSTICS
                , pluginInterface.IsDev
#endif
                ));
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
#if BCEF_DIAGNOSTICS
            timing.Next(UpdateTiming.Phase.PlayerPublication);
#endif
            // ObjectTable is read on the framework thread only. The draw-object field
            // follows the pinned GameObject layout; callbacks receive an immutable identity.
            var player = objects.LocalPlayer;
            var address = player?.Address ?? 0;
            var entityId = player?.EntityId ?? 0;
            var validPlayer = GameAudioLayout.TryReadActor(address, GameAudioLayout.GameObjectSize, entityId, out var fields);
            if (!validPlayer) address = 0;
            capture.SetPlayer(address, validPlayer ? entityId : 0, fields.DrawObject,
                validPlayer ? player?.Position : null, validPlayer && player?.CurrentMount != null);
#if BCEF_DIAGNOSTICS
            timing.Next(UpdateTiming.Phase.RosterArena);
#endif
            UpdateEnemyActors(capture, validPlayer);
#if BCEF_DIAGNOSTICS
            timing.Next(UpdateTiming.Phase.ScreenEffects);
#endif
            capture.UpdateScreenEffects(address != 0 && !clientState.IsPvP && !condition[ConditionFlag.BetweenAreas] &&
                !condition[ConditionFlag.BetweenAreas51] && Native.IsCurrentProcessForeground(), clientState.TerritoryType, address);
#if BCEF_DIAGNOSTICS
            timing.Next(UpdateTiming.Phase.RecordingSample);
#endif
#if BCEF_DIAGNOSTICS
            recording.Sample(capture, Volatile.Read(ref hapticSettings), objects);
#endif
        }
        else
        {
#if BCEF_DIAGNOSTICS
            timing.Next(UpdateTiming.Phase.RosterArena);
#endif
            UpdateEnemyActors(null, (objects.LocalPlayer?.Address ?? 0) != 0);
        }
#if BCEF_DIAGNOSTICS
        timing.Next(UpdateTiming.Phase.SaveGuard);
#endif
        saves.TrySaveDue(clock.Elapsed.TotalSeconds);
        if (!reportedGuardError && guard.Errors > 0)
        {
            reportedGuardError = true;
            log.Warning("A controller output could not be inspected. Original game output was passed through.");
        }
    }

    private Vector3? ActionTargetPosition(ulong id)
    {
        // Receive runs on the game's update path. Off-thread callbacks retain
        // emitter distance instead of traversing a concurrently changing table.
        if (!framework.IsInFrameworkUpdateThread) return null;
        var target = objects.SearchById(id);
        return target != null && target.IsValid() && target.GameObjectId == id ? target.Position : null;
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
        if (enemyDuty == 0 && (capture == null || !allowed
#if BCEF_DIAGNOSTICS
            && !recording.Active
#endif
            ))
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
                var combatNpc = actor.SubKind == (byte)BattleNpcSubKind.Combatant;
                var battleActor = combatNpc || actor.SubKind == (byte)BattleNpcSubKind.BNpcPart;
                if (!GameAudioLayout.TryReadActor(actor.Address, GameAudioLayout.GameObjectSize, actor.EntityId, out var fields)) continue;
                if (combatNpc)
                {
                    var category = localPlayer != null ? GameTargetRelation.Read(actor.Address) : null;
                    if (!GameTargetRelation.IsEnemy(category))
                    {
                        // Allies and unknown relationships must not enter regular
                        // history or become enemies through encounter promotion.
                        playerSources[playerSourceCount++] = actor.EntityId;
                        if (!category.HasValue && haptics.EnemiesEnabled && !reportedEnemyRelationError)
                        {
                            reportedEnemyRelationError = true;
                            log.Warning("Enemy target classification is unavailable; combat NPC audio is skipped.");
                        }
                        continue;
                    }
                }
                var combatant = combatNpc || battleActor && (actor.StatusFlags & StatusFlags.Hostile) != 0;
                // Membership and NPC rank are both required; nearby FATE trash is not a boss.
                var boss = enemyEncounters.IsBoss(enemyDuty, actor.NameId, actor.BaseId, fields.FateId);
                // The shared roster selector requires a live boss with the same
                // NPC name. This also supports helpers for open-world bosses.
                var helper = actor.SubKind == GameAudioLayout.BossMechanicSubKind;
                var owner = actor.OwnerId;
                var bossOwnerName = battleActor ? BossMechanics.OwnerName(enemyDuty, actor.NameId, actor.BaseId) : 0;
                var summon = battleActor && (BossMechanics.HasOwner(owner) || bossOwnerName != 0);
                enemyCandidates[count++] = new(actor.Address, actor.EntityId, fields.DrawObject, actor.ObjectIndex,
                    combatant && !boss ? EnemySoundKind.Regular : EnemySoundKind.Boss,
                    combatant ? EnemyActorRole.Combatant : helper ? EnemyActorRole.BossMechanic :
                        summon ? EnemyActorRole.BossSummon : EnemyActorRole.Unclassified,
                    actor.NameId, actor.BaseId, owner, bossOwnerName, fields.EventId, fields.LayoutId);
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
        var selected = bossEncounter.SelectRoster(enemyCandidates.AsSpan(0, count), enemyActors);
        capture?.SetEnemies(enemyActors.AsSpan(0, selected), territory, allowed
#if BCEF_DIAGNOSTICS
            ,
            new(enemyDuty, enemyQueueSize, enemyPartySize, enemyAllianceParties, playerInCombat, bossInCombat,
                bossEncounter.Active, bossEncounter.Count, bossEncounter.Saturated)
#endif
            );
        // Publish the roster first so a newly discovered actor retains this
        // cast on its existing identity without changing roster equality.
        if (capture != null)
        {
            for (var i = 0; i < selected; i++)
            {
                var source = enemyActors[i];
                if (source.ObjectIndex < 0 || source.ObjectIndex >= objects.Length) continue;
                if (objects[source.ObjectIndex] is IBattleChara casting && casting.Address == source.Actor &&
                    casting.EntityId == source.EntityId && casting.IsCasting && casting.CastActionType == 1)
                    capture.ObserveEnemyCast(source.Actor, source.EntityId, casting.CastActionId, casting.CastActionType);
            }
        }
        capture?.UpdateArena(territory, allowed && bossEncounter.Active &&
            (haptics.BossEnabled
#if BCEF_DIAGNOSTICS
                || recording.Active
#endif
            ),
            prepare: playerPresent && !clientState.IsPvP && enemyDuty != 0 && (haptics.BossEnabled
#if BCEF_DIAGNOSTICS
                || recording.Active
#endif
            ));
    }

    private void Changed()
    {
        PublishSettings();
        saves.MarkDirty(clock.Elapsed.TotalSeconds);
    }

    private void PublishSettings()
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
    }

    private void Save()
    {
        saves.SaveNow();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Cleanup("shutdown", Shutdown);
        log.Information("{PluginName} unloaded; resource cleanup attempted.", PluginInfo.Name);
    }

    private void Shutdown()
    {
        // Also gate callbacks retained after a failed constructor or detach.
        // A callback already in progress remains owned by the host's lifecycle.
        Interlocked.Exchange(ref disposed, 1);
        ResourceCleanup.Run(
            () => framework.Update -= Update,
            () => pluginInterface.UiBuilder.Draw -= Draw,
            () => pluginInterface.UiBuilder.OpenConfigUi -= Open,
            () => pluginInterface.UiBuilder.OpenMainUi -= Open,
            () => { if (commandRegistered) { commands.RemoveHandler(Command); commandRegistered = false; } },
            () => Volatile.Write(ref windowOpen, false),
            () => Volatile.Write(ref setupReady, false),
            () => { if (settings != null) Volatile.Write(ref settings, settings with { Enabled = false }); },
            () => { if (hapticSettings != null) Volatile.Write(ref hapticSettings, hapticSettings with
                { Enabled = false, BossEnabled = false, RegularEnemyEnabled = false, FootstepAmplifier = false, HitReactions = false, ScreenEffects = false }); },
            () => Volatile.Write(ref limitBreakState, LimitBreakState.Empty),
#if BCEF_DIAGNOSTICS
            () => recording.StopTrace(),
#endif
            () => stop.Cancel(),
            () => worker?.GetAwaiter().GetResult(),
            () => DisposeInitialization(audioInitialization),
            () => DisposeInitialization(limitBreakInitialization),
#if BCEF_DIAGNOSTICS
            () => recording.Shutdown(),
#endif
            () => rumble?.Dispose(),
            () => bluetoothRumble?.Dispose(),
            () => rumbleDevices?.Dispose(),
            () => guard?.Dispose(),
            () => session?.Dispose(),
            () => saves.TrySaveDue(clock.Elapsed.TotalSeconds, force: true),
            () => ui?.Dispose(),
            () => stop.Dispose());
    }

    private void Cleanup(string stage, Action action)
    {
        try { action(); }
        catch (Exception error) { LogCleanupError(stage, error); }
    }

    private void LogCleanupError(string stage, Exception error)
    {
        // Logging must never interrupt release of another resource.
        try { log.Warning(error, "Plugin cleanup failed during {Stage}.", stage); } catch { }
    }

    private static void DisposeInitialization<T>(Task<T>? initialization) where T : IDisposable
    {
        if (initialization == null) return;
        // A faulted initializer already cleaned its partial resources. Surface
        // the failure to the aggregate while allowing all other joins/disposals.
        initialization.GetAwaiter().GetResult().Dispose();
    }
}
