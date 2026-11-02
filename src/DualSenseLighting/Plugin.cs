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
using DualSenseLighting.Ui;

[assembly: System.Reflection.AssemblyVersion("0.3.1.0")]

namespace DualSenseLighting;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/ds5light";
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
    private readonly ControllerPreview preview;
    private LightingSettings settings;
    private bool windowOpen;
    private double saveAt = double.PositiveInfinity;
    private string? lastError;
    private bool reportedGuardError;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands,
        IFramework framework, IGameInteropProvider interop, IPluginLog log, ITextureProvider textures)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        this.log = log;
        if (pluginInterface.InstalledPlugins.Any(p => p.InternalName == "SteamLightingGuard" && p.IsLoaded))
            throw new InvalidOperationException("Disable the older Steam Lighting Guard before loading DualSense Lighting.");
        configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        settings = configuration.Snapshot();
        preview = new ControllerPreview(textures);
        session = new LightingSession(new HidDevices(), Native.IsCurrentProcessForeground, ReportError,
            (_, _) => new LightingFrame(0, 0, 0, 0));
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
            preview.Draw(Volatile.Read(ref settings), foreground, clock.Elapsed.TotalSeconds);
            ImGui.TextUnformatted(foreground ? "FFXIV active" : "Background protected — custom output paused");
            ImGui.TextUnformatted($"Connected USB controllers: {session.DeviceCount}");
            ImGui.Separator();
            var enabled = configuration.Enabled;
            if (ImGui.Checkbox("Enable custom lighting", ref enabled)) { configuration.Enabled = enabled; Changed(); }
            var effect = (int)settings.Effect;
            ImGui.Separator();
            ImGui.TextDisabled("Lightbar");
            if (ImGui.Combo("Lightbar effect", ref effect, "Solid\0Rainbow\0Breathing\0"))
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
                if (ImGui.SliderFloat("Lightbar cycle (seconds)", ref cycle, 1, 60, "%.1f"))
                { configuration.CycleSeconds = cycle; Changed(); }
            }
            ImGui.Separator();
            ImGui.TextDisabled("Player indicators");
            var player = PlayerIndicatorEffects.Count(settings.PlayerLights);
            if (ImGui.Combo("Indicator count", ref player, "Off\0One light\0Two lights\0Three lights\0Four lights\0Five lights\0"))
            { configuration.PlayerLights = PlayerIndicatorEffects.MaskForCount(player); Changed(); }
            var animation = (int)settings.PlayerAnimation;
            if (ImGui.Combo("Indicator animation", ref animation, "Steady\0Chase\0Bounce\0Blink\0Heartbeat\0Sparkle\0Loading\0"))
            { configuration.PlayerAnimation = (PlayerIndicatorAnimation)animation; Changed(); }
            if (settings.PlayerAnimation != PlayerIndicatorAnimation.Steady)
            {
                var cycle = settings.PlayerCycleSeconds;
                if (ImGui.SliderFloat("Indicator cycle (seconds)", ref cycle, 1, 12, "%.1f"))
                { configuration.PlayerCycleSeconds = cycle; Changed(); }
                ImGui.TextDisabled("Animation stays within the selected indicator positions.");
            }
            if (player == 0) ImGui.TextDisabled("Choose an indicator count to light the strip.");
            else if (player == 1 && settings.PlayerAnimation is PlayerIndicatorAnimation.Chase or PlayerIndicatorAnimation.Bounce)
                ImGui.TextDisabled("Choose two or more indicators to see movement.");
            ImGui.Separator();
            ImGui.TextWrapped("Custom lighting runs only while FFXIV is active. On leaving, its lights turn off once and controller handles close so any other lighting app can take over. Background protection is always active. Native haptics, speaker and trigger effects pass through.");
            ImGui.TextDisabled("Changes are saved automatically.");
            if (ImGui.CollapsingHeader("Diagnostics"))
            {
                ImGui.TextUnformatted($"Lighting writes: {session.WriteCount}");
                ImGui.TextUnformatted($"Lighting releases: {session.HandoffCount}");
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
