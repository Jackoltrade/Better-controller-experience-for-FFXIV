// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;
using DualSenseLighting.Windows;

[assembly: System.Reflection.AssemblyVersion("0.1.1.0")]

namespace DualSenseLighting;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/ds5light";
    private static readonly byte[] PlayerMasks = [0, 4, 10, 21, 27, 31];
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Configuration configuration;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource stop = new();
    private readonly LightingSession session;
    private readonly GameOutputGuard guard;
    private readonly Task worker;
    private LightingSettings settings;
    private volatile bool restoreSteamLighting;
    private bool windowOpen;
    private double saveAt = double.PositiveInfinity;
    private string? lastError;
    private bool reportedGuardError;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands,
        IFramework framework, IGameInteropProvider interop, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        this.log = log;
        if (pluginInterface.InstalledPlugins.Any(p => p.InternalName == "SteamLightingGuard" && p.IsLoaded))
            throw new InvalidOperationException("Disable the older Steam Lighting Guard before loading DualSense Lighting.");
        configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        settings = configuration.Snapshot();
        restoreSteamLighting = configuration.RestoreSteamLighting;
        var steam = new SteamLightingProfiles(SteamInstallation.GetRoot(), SteamInstallation.GetActiveAccount);
        session = new LightingSession(new HidDevices(), Native.IsCurrentProcessForeground, ReportError,
            (device, count) => restoreSteamLighting ? steam.Read(device.Serial, count) : null);
        guard = new GameOutputGuard(interop, () => Volatile.Read(ref settings), () => clock.Elapsed.TotalSeconds);
        try
        {
            commands.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Configure DualSense lighting while FFXIV is active." });
            pluginInterface.UiBuilder.Draw += Draw;
            pluginInterface.UiBuilder.OpenConfigUi += Open;
            pluginInterface.UiBuilder.OpenMainUi += Open;
            framework.Update += Update;
            worker = Task.Run(RunWorker);
            log.Information("DualSense Lighting enabled. USB lighting is foreground-only; background game LED commands are guarded.");
        }
        catch
        {
            commands.RemoveHandler(Command);
            pluginInterface.UiBuilder.Draw -= Draw;
            pluginInterface.UiBuilder.OpenConfigUi -= Open;
            pluginInterface.UiBuilder.OpenMainUi -= Open;
            framework.Update -= Update;
            guard.Dispose();
            session.Dispose();
            stop.Dispose();
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
                await session.TickAsync(Volatile.Read(ref settings), clock.Elapsed.TotalSeconds, stop.Token).ConfigureAwait(false);
            } while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { ReportError(error); }
        finally { await session.ReleaseAsync(true, CancellationToken.None).ConfigureAwait(false); }
    }

    private void ReportError(Exception error)
    {
        var message = error.Message;
        if (Interlocked.Exchange(ref lastError, message) != message)
            log.Warning(error, "DualSense lighting output paused; reconnecting devices will be retried.");
    }

    private void OnCommand(string command, string args) => Open();
    private void Open() => windowOpen = true;

    private void Update(IFramework _)
    {
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
        restoreSteamLighting = configuration.RestoreSteamLighting;
        saveAt = clock.Elapsed.TotalSeconds + 0.5;
    }

    private void Save()
    {
        pluginInterface.SavePluginConfig(configuration);
        saveAt = double.PositiveInfinity;
    }

    private void Draw()
    {
        if (!windowOpen) return;
        ImGui.SetNextWindowSize(new Vector2(450, 0), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("DualSense Lighting###DualSenseLightingConfig", ref windowOpen, ImGuiWindowFlags.AlwaysAutoResize))
        { ImGui.End(); return; }
        try
        {
            var foreground = Native.IsCurrentProcessForeground();
            ImGui.TextUnformatted(foreground ? "FFXIV active" : "Background protected — custom output paused");
            ImGui.TextUnformatted($"Connected USB controllers: {session.DeviceCount}");
            ImGui.Separator();
            var enabled = configuration.Enabled;
            if (ImGui.Checkbox("Enable custom lighting", ref enabled)) { configuration.Enabled = enabled; Changed(); }
            var restore = configuration.RestoreSteamLighting;
            if (ImGui.Checkbox("Restore Steam lighting when leaving FFXIV", ref restore))
            { configuration.RestoreSteamLighting = restore; Changed(); }
            var effect = (int)settings.Effect;
            if (ImGui.Combo("Effect", ref effect, "Solid\0Rainbow\0Breathing\0"))
            { configuration.Effect = (LightingEffect)effect; Changed(); }
            if (settings.Effect != LightingEffect.Rainbow)
            {
                var color = new Vector3(configuration.Red, configuration.Green, configuration.Blue) / 255;
                if (ImGui.ColorEdit3("Color", ref color))
                {
                    configuration.Red = (byte)Math.Clamp((int)Math.Round(color.X * 255), 0, 255);
                    configuration.Green = (byte)Math.Clamp((int)Math.Round(color.Y * 255), 0, 255);
                    configuration.Blue = (byte)Math.Clamp((int)Math.Round(color.Z * 255), 0, 255);
                    Changed();
                }
            }
            var brightness = settings.Brightness;
            if (ImGui.SliderFloat("Brightness", ref brightness, 0, 1, "%.2f"))
            { configuration.Brightness = brightness; Changed(); }
            if (settings.Effect != LightingEffect.Solid)
            {
                var cycle = settings.CycleSeconds;
                if (ImGui.SliderFloat("Cycle (seconds)", ref cycle, 1, 60, "%.1f"))
                { configuration.CycleSeconds = cycle; Changed(); }
            }
            var player = Array.IndexOf(PlayerMasks, settings.PlayerLights);
            if (player < 0) player = 0;
            if (ImGui.Combo("Player indicator", ref player, "Off\0One light\0Two lights\0Three lights\0Four lights\0Five lights\0"))
            { configuration.PlayerLights = PlayerMasks[player]; Changed(); }
            ImGui.Separator();
            ImGui.TextWrapped("Custom effects run while FFXIV is active. On leaving, saved Steam lighting is restored once if enabled, then other apps control the LEDs. Native haptics, speaker and trigger effects pass through.");
            ImGui.TextDisabled("Changes are saved automatically.");
            if (ImGui.CollapsingHeader("Diagnostics"))
            {
                ImGui.TextUnformatted($"Lighting writes: {session.WriteCount}");
                ImGui.TextUnformatted($"Steam handoffs: {session.HandoffCount}");
                ImGui.TextUnformatted($"Foreground reports: {guard.ForegroundReports}");
                ImGui.TextUnformatted($"Background reports guarded: {guard.BackgroundReports}");
                ImGui.TextUnformatted($"Inspection errors: {guard.Errors}");
                var error = Volatile.Read(ref lastError);
                if (error != null) ImGui.TextWrapped($"Last output error: {error}");
            }
            if (ImGui.Button("Close")) windowOpen = false;
        }
        finally { ImGui.End(); }
    }

    public void Dispose()
    {
        framework.Update -= Update;
        pluginInterface.UiBuilder.Draw -= Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= Open;
        pluginInterface.UiBuilder.OpenMainUi -= Open;
        commands.RemoveHandler(Command);
        Volatile.Write(ref settings, Volatile.Read(ref settings) with { Enabled = false });
        stop.Cancel();
        worker.GetAwaiter().GetResult();
        guard.Dispose();
        stop.Dispose();
        if (!double.IsPositiveInfinity(saveAt)) Save();
        log.Information("DualSense Lighting unloaded; shared device handles closed.");
    }
}
