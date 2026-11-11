// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

// Exercise the compiled UI constructor and command entry points, including
// saved recording consent copied between normal, testing and dev installs.
internal static class DiagnosticsAccessChecks
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        if (!ProductionBoundaryChecks.Diagnostics(plugin))
        {
            ProductionBoundaryChecks.Run(check, plugin);
            return;
        }
        var uiType = plugin.GetType("DualSenseLighting.Ui.SettingsUi", true)!;
        var configType = plugin.GetType("DualSenseLighting.Configuration", true)!;
        var recorderType = plugin.GetType("DualSenseLighting.DiagnosticRecordingCoordinator", true)!;
        var pluginType = plugin.GetType("DualSenseLighting.Plugin", true)!;
        foreach (var dev in new[] { false, true })
            foreach (var testing in new[] { false, true })
            {
                object Proxy(string name) => DispatchProxy.Create(Type.GetType(name + ", Dalamud", true)!, typeof(WindowingChecks.UiServiceProxy));
                var builder = Proxy("Dalamud.Interface.IUiBuilder");
                var atlas = Proxy("Dalamud.Interface.ManagedFontAtlas.IFontAtlas");
                var font = Proxy("Dalamud.Interface.ManagedFontAtlas.IFontHandle");
                ((WindowingChecks.UiServiceProxy)builder).Atlas = atlas;
                ((WindowingChecks.UiServiceProxy)atlas).Font = font;
                var service = Proxy("Dalamud.Plugin.IDalamudPluginInterface");
                var origin = (WindowingChecks.UiServiceProxy)service;
                origin.Builder = builder; origin.IsDev = dev; origin.IsTesting = testing;
                var config = Activator.CreateInstance(configType)!;
                configType.GetMethod("CompleteSetup")!.Invoke(config, new object[] { true, true });
                configType.GetProperty("HasAcknowledgedRecording")!.SetValue(config, true);
                var recorder = RuntimeHelpers.GetUninitializedObject(recorderType);
                var measurements = new List<bool>();
                var ui = WindowingChecks.CreateSettingsUi(plugin, service, config, recorder, measurements.Add);
                try
                {
                    object? Field(string name) => uiType.GetField(name, All)!.GetValue(ui);
                    bool Flag(string name) => (bool)Field(name)!;
                    int Requests() => (int)recorderType.GetField("traceToggleRequested", All)!.GetValue(recorder)!;
                    void ClearRequests() => recorderType.GetField("traceToggleRequested", All)!.SetValue(recorder, 0);
                    check((bool)uiType.GetProperty("DiagnosticsAvailable", All)!.GetValue(ui)! == dev,
                        "Diagnostics availability followed testing/build state instead of dev-install origin");
                    ((Action<bool>)Field("measureUpdates")!)(true);
                    check(measurements.SequenceEqual(new[] { dev }), "A normal install activated framework timing");
                    var accepted = (bool)uiType.GetMethod("RequestTraceToggle", All)!.Invoke(ui, null)!;
                    check(accepted == dev && Requests() == (dev ? 1 : 0) && Flag("selectDiagnostics") == dev,
                        "Saved consent bypassed the dev recording gate or dev recording lost its intent");
                    check(!Flag("showRecordingNotice") && !Flag("showGeneralInfo"),
                        "A denied diagnostic request opened consent or changed completed setup");
                    ClearRequests();

                    var instance = RuntimeHelpers.GetUninitializedObject(pluginType);
                    void Set(string name, object value) => pluginType.GetField(name, All)!.SetValue(instance, value);
                    var chat = DispatchProxy.Create(Type.GetType("Dalamud.Plugin.Services.IChatGui, Dalamud", true)!, typeof(ChatProxy));
                    var messages = ((ChatProxy)chat).Messages;
                    Set("ui", ui); Set("chat", chat);
                    Set("uiText", Activator.CreateInstance(plugin.GetType("DualSenseLighting.Ui.UiText", true)!, nonPublic: true)!);
                    pluginType.GetMethod("PrintCommandHelp", All)!.Invoke(instance, null);
                    check(messages.Any(message => message.Contains("/bcef trace", StringComparison.Ordinal)) == dev,
                        "Normal command help advertised hidden diagnostics or dev help lost trace");
                    messages.Clear();
                    pluginType.GetMethod("OnCommand", All)!.Invoke(instance, new object[] { "/bcef", "trace" });
                    check(Requests() == (dev ? 1 : 0) && (bool)pluginType.GetField("windowOpen", All)!.GetValue(instance)! == dev,
                        "The trace command bypassed install-origin gating or failed to open the dev UI");
                    check(dev || messages.Any(message => message.Contains("development installs", StringComparison.Ordinal)),
                        "A denied trace command gave no explanation");
                    ClearRequests();

                    configType.GetProperty("HasAcknowledgedRecording")!.SetValue(config, false);
                    accepted = (bool)uiType.GetMethod("RequestTraceToggle", All)!.Invoke(ui, null)!;
                    check(accepted == dev && Flag("showRecordingNotice") == dev && Requests() == 0,
                        "Dev recording skipped consent or a normal install entered recording consent");
                    pluginType.GetMethod("OnCommand", All)!.Invoke(instance, new object[] { "/bcef", "info" });
                    check(Flag("showGeneralInfo") && (bool)pluginType.GetField("windowOpen", All)!.GetValue(instance)!,
                        "Diagnostics gating blocked the normal Info command");
                }
                finally { uiType.GetMethod("Dispose", All)!.Invoke(ui, null); }
            }
    }

    public class ChatProxy : DispatchProxy
    {
        internal readonly List<string> Messages = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name is "Print" or "PrintError") Messages.Add(args?[0]?.ToString() ?? "");
            return method.ReturnType != typeof(void) && method.ReturnType.IsValueType
                ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }
}
