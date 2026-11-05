// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

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
        CheckOwnerFocusHandoff(check, plugin, windowBase, systemType, type, dialogType);
        CheckVisibilityLifecycle(check, plugin, windowBase);
        DiagnosticsAccessChecks.Run(check, plugin);
        var native = plugin.GetType("DualSenseLighting.Windows.Native", true)!;
        var physicalKey = native.GetMethod("GetAsyncKeyState", BindingFlags.Static | BindingFlags.NonPublic)!;
        var import = physicalKey.GetCustomAttribute<System.Runtime.InteropServices.DllImportAttribute>();
        check(physicalKey.ReturnType == typeof(short) && physicalKey.GetParameters().Length == 1 &&
            physicalKey.GetParameters()[0].ParameterType == typeof(int) && import?.Value == "user32.dll",
            "Physical release observation lost Win32 SHORT(int) ABI");
    }

    private static void CheckVisibilityLifecycle(Action<bool, string> check, Assembly plugin, Type windowBase)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object Proxy(string typeName) => DispatchProxy.Create(Type.GetType(typeName + ", Dalamud", true)!, typeof(UiServiceProxy));
        var builder = Proxy("Dalamud.Interface.IUiBuilder"); var builderState = (UiServiceProxy)builder;
        var atlas = Proxy("Dalamud.Interface.ManagedFontAtlas.IFontAtlas");
        var font = Proxy("Dalamud.Interface.ManagedFontAtlas.IFontHandle");
        ((UiServiceProxy)atlas).Font = font; builderState.Atlas = atlas;
        var pluginInterface = Proxy("Dalamud.Plugin.IDalamudPluginInterface");
        ((UiServiceProxy)pluginInterface).Builder = builder;
        var ownerType = plugin.GetType("DualSenseLighting.Ui.SettingsUi", true)!;
        object Construct() => CreateSettingsUi(plugin, pluginInterface);
        var owner = Construct();
        var settings = ownerType.GetField("settingsWindow", all)!.GetValue(owner)!;
        var dialog = ownerType.GetField("dialogWindow", all)!.GetValue(owner)!;
        var open = windowBase.GetProperty("IsOpen")!;
        var request = windowBase.GetProperty("RequestFocus")!;
        var keyInterface = Type.GetType("Dalamud.Plugin.Services.IKeyState, Dalamud", true)!;
        var key = DispatchProxy.Create(keyInterface, typeof(KeyStateProxy)); var keys = (KeyStateProxy)key;
        var process = ownerType.GetMethod("ProcessInput", all, null, new[] { keyInterface, typeof(Func<bool>) }, null)!;
        bool physicalDown = true; Func<bool> physical = () => physicalDown;
        void Input() => process.Invoke(owner, new object[] { key, physical });
        open.SetValue(settings, true); open.SetValue(dialog, true); request.SetValue(dialog, true);
        builderState.Raise("HideUi"); keys.EscapeDown = true; Input();
        check(keys.EscapeDown && (bool)open.GetValue(dialog)!, "Hidden dialog acquired a new game Escape");
        builderState.Raise("ShowUi"); Input();
        check(!keys.EscapeDown && !(bool)open.GetValue(dialog)!, "Showing UI did not restore focused-dialog input eligibility");
        builderState.Raise("HideUi"); keys.EscapeDown = true; Input();
        check(!keys.EscapeDown, "Hiding UI stopped clearing an already-consumed held Escape");
        physicalDown = false; keys.EscapeDown = true; Input();
        keys.EscapeDown = true; Input();
        check(keys.EscapeDown, "Hidden no-Draw keyup left suppression active for a separate press");
        builderState.FailHideRemoval = true;
        try { ownerType.GetMethod("Dispose", all)!.Invoke(owner, null); check(false, "Injected event-removal failure was lost"); }
        catch (TargetInvocationException error)
        { check(error.InnerException != null, "Dispose lost cleanup failure"); }
        check(builderState.Events.Values.All(value => value == null) && ((UiServiceProxy)font).Disposals == 1,
            "Visibility unsubscribe failure skipped another event or font cleanup");
        ownerType.GetMethod("Dispose", all)!.Invoke(owner, null);
        check(((UiServiceProxy)font).Disposals == 1, "Settings UI disposal repeated acquired font cleanup");
        builderState.FailHideRemoval = false; builderState.FailShowSubscription = true;
        try { Construct(); check(false, "Injected ShowUi subscription failure did not reach the actual constructor"); }
        catch (TargetInvocationException error)
        { check(error.InnerException is InvalidOperationException && error.InnerException.Message == "Injected ShowUi subscription failure",
            "Constructor cleanup masked the original visibility-subscription failure"); }
        check(builderState.Events.Values.All(value => value == null) && ((UiServiceProxy)font).Disposals == 2,
            "Actual constructor rollback retained visibility callbacks or its acquired font");
    }

    public class UiServiceProxy : DispatchProxy
    {
        internal object? Builder, Atlas, Font;
        internal bool FailShowSubscription, FailHideRemoval, IsDev, IsTesting;
        internal int Disposals;
        internal readonly System.Collections.Generic.Dictionary<string, Delegate?> Events = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var name = method!.Name;
            if (name == "get_IsDev") return IsDev;
            if (name == "get_IsTesting") return IsTesting;
            if (name == "get_UiBuilder") return Builder;
            if (name == "get_FontAtlas") return Atlas;
            if (name == "NewDelegateFontHandle") return Font;
            if (name == "Dispose") { Disposals++; return null; }
            if (name.StartsWith("add_", StringComparison.Ordinal))
            {
                var eventName = name.Substring(4);
                Events.TryGetValue(eventName, out var old);
                Events[eventName] = Delegate.Combine(old, (Delegate)args![0]!);
                if (eventName == "ShowUi" && FailShowSubscription)
                    throw new InvalidOperationException("Injected ShowUi subscription failure");
                return null;
            }
            if (name.StartsWith("remove_", StringComparison.Ordinal))
            {
                var eventName = name.Substring(7);
                Events.TryGetValue(eventName, out var old);
                Events[eventName] = Delegate.Remove(old, (Delegate)args![0]!);
                if (eventName == "HideUi" && FailHideRemoval) throw new InvalidOperationException("Injected HideUi removal failure");
                return null;
            }
            return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
        internal void Raise(string name) { if (Events.TryGetValue(name, out var handler)) handler?.DynamicInvoke(); }
    }

    internal static object CreateSettingsUi(Assembly plugin, object pluginInterface,
        object? configuration = null, object? recording = null, Action<bool>? measure = null)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = plugin.GetType("DualSenseLighting.Ui.SettingsUi", true)!;
        var constructor = type.GetConstructors(all).Single();
        var arguments = constructor.GetParameters().Select(parameter => parameter.Name switch
        {
            "configuration" => configuration ?? Activator.CreateInstance(plugin.GetType("DualSenseLighting.Configuration", true)!),
            "uiText" => Activator.CreateInstance(plugin.GetType("DualSenseLighting.Ui.UiText", true)!, nonPublic: true),
            "pluginInterface" => pluginInterface,
            "clock" => new System.Diagnostics.Stopwatch(),
            "recording" => recording,
            "measureUpdates" => measure,
            _ => null,
        }).ToArray();
        return constructor.Invoke(arguments);
    }

    public class KeyStateProxy : DispatchProxy
    {
        internal bool EscapeDown;
        internal int Clears;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_Item")
            {
                if (Convert.ToInt32(args![0]) != 27) throw new InvalidOperationException("Read unrelated key");
                return EscapeDown;
            }
            if (method.Name == "set_Item")
            {
                if (Convert.ToInt32(args![0]) != 27 || (bool)args[1]!)
                    throw new InvalidOperationException("Only Escape suppression is permitted");
                EscapeDown = false; Clears++; return null;
            }
            throw new NotSupportedException(method.Name);
        }
    }

    private static void CheckOwnerFocusHandoff(Action<bool, string> check, Assembly plugin, Type windowBase,
        Type systemType, Type settingsType, Type dialogType)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var ownerType = plugin.GetType("DualSenseLighting.Ui.SettingsUi", true)!;
        var owner = RuntimeHelpers.GetUninitializedObject(ownerType);
        var system = Activator.CreateInstance(systemType, new object[] { "OwnerFocusChecks" })!;
        var settings = settingsType.GetConstructor(instance, null, new[] { typeof(Action) }, null)!
            .Invoke(new object[] { (Action)(() => { }) });
        var dialog = dialogType.GetConstructor(instance, null, new[] { typeof(Action) }, null)!
            .Invoke(new object[] { (Action)(() => { }) });
        void Set(string name, object value) => ownerType.GetField(name, instance)!.SetValue(owner, value);
        Set("windowSystem", system); Set("settingsWindow", settings); Set("dialogWindow", dialog);
        var textType = plugin.GetType("DualSenseLighting.Ui.UiText", true)!;
        Set("uiText", Activator.CreateInstance(textType, nonPublic: true)!);
        var configType = plugin.GetType("DualSenseLighting.Configuration", true)!;
        var config = Activator.CreateInstance(configType)!; Set("configuration", config);
        var remove = systemType.GetMethod("RemoveAllWindows")!;
        try
        {
            // Execute the real constructor's granular registration operation;
            // no font, renderer, game state or hook is needed for this boundary.
            ownerType.GetMethod("RegisterWindows", instance)!.Invoke(owner, null);
            var registered = ((IEnumerable)systemType.GetProperty("Windows")!.GetValue(system)!).Cast<object>().ToArray();
            check(registered.Length == 2 && ReferenceEquals(registered[0], dialog) && ReferenceEquals(registered[1], settings),
                "Dialog close processing must precede the parent's focus update");
            var host = ((IEnumerable)systemType.GetField("windowHosts", instance)!.GetValue(system)!).Cast<object>().First();
            var hostType = host.GetType();
            var draw = hostType.GetMethod("DrawInternal", instance)!;
            var parametersType = draw.GetParameters()[0].ParameterType;
            var parameters = Activator.CreateInstance(parametersType)!;
            var flags = parametersType.GetProperty("Flags", instance)!;
            flags.SetValue(parameters, Enum.Parse(flags.PropertyType, "IsReducedMotion"));
            var open = windowBase.GetProperty("IsOpen")!;
            var focused = windowBase.GetProperty("IsFocused")!;
            var request = windowBase.GetProperty("RequestFocus")!;
            var kindType = ownerType.GetNestedType("DialogKind", BindingFlags.NonPublic)!;
            foreach (var kind in Enum.GetValues(kindType))
            {
                open.SetValue(settings, true); request.SetValue(settings, false);
                ownerType.GetMethod("OpenDialog", instance)!.Invoke(owner, new[] { kind });
                check((bool)open.GetValue(dialog)! && (bool)request.GetValue(dialog)!,
                    "Opening a dialog lost its native focus request");
                // Native Escape/title-X/button closing all leave this same
                // closed-host transition for the following frame.
                open.SetValue(dialog, false); focused.SetValue(dialog, true);
                hostType.GetField("internalLastIsOpen", instance)!.SetValue(host, true);
                draw.Invoke(host, new object?[] { parameters, null });
                check((bool)open.GetValue(settings)! && (bool)request.GetValue(settings)! &&
                    !(bool)open.GetValue(dialog)! && !(bool)focused.GetValue(dialog)!,
                    "Native dialog OnClose failed to request parent focus before the parent host draws");
            }
            var keyInterface = Type.GetType("Dalamud.Plugin.Services.IKeyState, Dalamud", true)!;
            var key = DispatchProxy.Create(keyInterface, typeof(KeyStateProxy));
            var keyState = (KeyStateProxy)key;
            var processInput = ownerType.GetMethod("ProcessInput", instance, null, new[] { keyInterface, typeof(Func<bool>) }, null)!;
            bool physicalDown = true; var polls = 0;
            Func<bool> physical = () => { polls++; return physicalDown; };
            var consumed = ownerType.GetField("consumeEscapeUntilRelease", instance)!;
            foreach (var kind in Enum.GetValues(kindType))
            {
                consumed.SetValue(owner, false);
                open.SetValue(settings, true); focused.SetValue(dialog, false);
                ownerType.GetMethod("OpenDialog", instance)!.Invoke(owner, new[] { kind });
                keyState.EscapeDown = true; var pollsBeforePress = polls;
                processInput.Invoke(owner, new object[] { key, physical });
                check(polls == pollsBeforePress, "Initial Escape consumption queried a potentially already-released physical key");
                check(!keyState.EscapeDown && !(bool)open.GetValue(dialog)! && (bool)open.GetValue(settings)!,
                    "Dialog Escape did not clear the game key before target cancel while retaining settings");
                keyState.EscapeDown = true; // Game input can republish held state each tick.
                processInput.Invoke(owner, new object[] { key, physical });
                check(!keyState.EscapeDown && (bool)consumed.GetValue(owner)!,
                    "Held dialog Escape leaked after the dialog closed");
            }
            // No Draw occurs during these updates: hidden/minimized UI must
            // still release ownership and allow the next separate game Escape.
            physicalDown = false; keyState.EscapeDown = true;
            processInput.Invoke(owner, new object[] { key, physical });
            check(!keyState.EscapeDown && !(bool)consumed.GetValue(owner)!,
                "Physical keyup without Draw left Escape suppression stuck");
            keyState.EscapeDown = true; var pollsAfterRelease = polls;
            processInput.Invoke(owner, new object[] { key, physical });
            check(keyState.EscapeDown && polls == pollsAfterRelease,
                "A separate parent/game Escape was swallowed after release");
            consumed.SetValue(owner, false);
            open.SetValue(settings, true); open.SetValue(dialog, true);
            focused.SetValue(dialog, false); request.SetValue(dialog, false);
            keyState.EscapeDown = true; var clears = keyState.Clears;
            processInput.Invoke(owner, new object[] { key, physical });
            check(keyState.EscapeDown && keyState.Clears == clears && (bool)open.GetValue(dialog)!,
                "An unfocused dialog consumed another window's Escape");
            open.SetValue(settings, false); request.SetValue(dialog, true);
            processInput.Invoke(owner, new object[] { key, physical });
            check(keyState.EscapeDown && keyState.Clears == clears,
                "A closed parent allowed its dialog to consume Escape");
            check(!(bool)configType.GetProperty("HasCompletedSetup")!.GetValue(config)! &&
                !(bool)configType.GetProperty("HasAcknowledgedRecording")!.GetValue(config)!,
                "Closing first-run/recording dialogs granted consent");
            open.SetValue(settings, false); request.SetValue(settings, false);
            dialogType.GetMethod("OnClose")!.Invoke(dialog, null);
            check(!(bool)open.GetValue(settings)! && !(bool)request.GetValue(settings)!,
                "Dialog close reopened or focused an already-closed parent");
            open.SetValue(dialog, true);
            settingsType.GetMethod("OnClose")!.Invoke(settings, null);
            check(!(bool)open.GetValue(dialog)!, "Closing the parent retained an orphan dialog");
            check((bool)windowBase.GetProperty("ShowCloseButton")!.GetValue(dialog)!,
                "Dialog focus handoff removed the title-bar close button");
        }
        finally { remove.Invoke(system, null); }
    }
}
