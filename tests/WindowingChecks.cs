// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;

// Load the compiled window against the installed Dalamud API. These managed
// operations do not construct Plugin, create ImGui state, or draw a WindowHost.
internal static class WindowingChecks
{
    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        var windowBase = Type.GetType("Dalamud.Interface.Windowing.Window, Dalamud", true)!;
        var type = plugin.GetType("DualSenseLighting.Ui.SettingsWindow", true)!;
        check(windowBase.IsAssignableFrom(type), "Compiled settings window does not use Dalamud's Window API");

        var calls = 0;
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(Action) }, null) ?? throw new MissingMethodException(type.FullName, ".ctor(Action)");
        var window = constructor.Invoke(new object[] { (Action)(() => calls++) });
        var name = (string)windowBase.GetProperty("WindowName")!.GetValue(window)!;
        check(name.EndsWith("###DualSenseLightingConfig", StringComparison.Ordinal),
            "Settings window lost its stable ImGui identity");
        var flags = windowBase.GetProperty("Flags")!.GetValue(window)!;
        var autoResize = Convert.ToUInt64(Enum.Parse(flags.GetType(), "AlwaysAutoResize"));
        check((Convert.ToUInt64(flags) & autoResize) != 0,
            "Settings window lost the automatic height used across configuration tabs");
        check(calls == 0, "Constructing the settings window unexpectedly drew plugin content");
        type.GetMethod("Draw", Type.EmptyTypes)!.Invoke(window, null);
        check(calls == 1, "Compiled settings window did not delegate one draw to its content callback");
        var closed = 0;
        type.GetProperty("Closed")!.SetValue(window, (Action)(() => closed++));
        type.GetMethod("OnClose", Type.EmptyTypes)!.Invoke(window, null);
        check(closed == 1, "Settings window did not notify its owner when WindowSystem closes it");

        var dialogType = plugin.GetType("DualSenseLighting.Ui.DialogWindow", true)!;
        check(windowBase.IsAssignableFrom(dialogType), "Compiled dialog does not use Dalamud's Window API");
        var dialogCalls = 0;
        var dialogConstructor = dialogType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(Action) }, null) ?? throw new MissingMethodException(dialogType.FullName, ".ctor(Action)");
        var dialog = dialogConstructor.Invoke(new object[] { (Action)(() => dialogCalls++) });
        var dialogFlags = windowBase.GetProperty("Flags")!.GetValue(dialog)!;
        var expectedFlags = Convert.ToUInt64(Enum.Parse(dialogFlags.GetType(), "AlwaysAutoResize, NoSavedSettings, NoCollapse"));
        check((Convert.ToUInt64(dialogFlags) & expectedFlags) == expectedFlags,
            "Dialog lost its automatic sizing, transient layout, or noncollapsible shell");
        check((bool)windowBase.GetProperty("RespectCloseHotkey")!.GetValue(dialog)!,
            "Dialog does not participate in Dalamud's native Escape handling");
        dialogType.GetProperty("Title")!.SetValue(dialog, "Windowing ABI check");
        check((string)windowBase.GetProperty("WindowName")!.GetValue(dialog)! ==
            "Windowing ABI check###DualSenseLightingDialog", "Dialog title changes lost the stable window identity");
        check(dialogCalls == 0, "Constructing the dialog unexpectedly drew plugin content");
        dialogType.GetMethod("Draw", Type.EmptyTypes)!.Invoke(dialog, null);
        check(dialogCalls == 1, "Compiled dialog did not delegate one draw to its content callback");
        // PostDraw may be reached without a preceding PreDraw. It must not pop
        // styles or require a native context when the shell pushed none.
        dialogType.GetMethod("PostDraw", Type.EmptyTypes)!.Invoke(dialog, null);

        var systemType = Type.GetType("Dalamud.Interface.Windowing.WindowSystem, Dalamud", true)!;
        var system = Activator.CreateInstance(systemType, new object[] { "DualSenseWindowingChecks" })!;
        var windows = systemType.GetProperty("Windows")!;
        object[] Registered() => ((IEnumerable)windows.GetValue(system)!).Cast<object>().ToArray();
        var add = systemType.GetMethod("AddWindow")!;
        var removeAll = systemType.GetMethod("RemoveAllWindows")!;
        try
        {
            add.Invoke(system, new[] { window });
            var registered = Registered();
            check(registered.Length == 1 && ReferenceEquals(registered[0], window),
                "Installed WindowSystem did not register the compiled settings window");
            add.Invoke(system, new[] { dialog });
            registered = Registered();
            check(registered.Length == 2 && registered.Any(item => ReferenceEquals(item, dialog)),
                "WindowSystem did not independently register the dialog");
            systemType.GetMethod("RemoveWindow")!.Invoke(system, new[] { window });
            registered = Registered();
            check(registered.Length == 1 && ReferenceEquals(registered[0], dialog),
                "Removing settings also removed or retained the wrong dialog window");
            add.Invoke(system, new[] { window });
            removeAll.Invoke(system, null);
            check(Registered().Length == 0, "WindowSystem cleanup retained a settings-window callback");
        }
        finally { removeAll.Invoke(system, null); }
    }
}
