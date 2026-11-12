// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// These fixtures call the compiled owners. Generated Hook subclasses only run
// managed callbacks: they never install/detach an OS hook or open a controller.
internal static unsafe class OwnerLifecycleChecks
{
    public class ServiceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }

    private static readonly Dictionary<Type, Type> HookTypes = new();
    private static readonly ModuleBuilder HookModule = CreateHookModule();
    private static readonly List<(uint Slot, ushort Low, ushort High)> Restored = new();
    [UnmanagedCallersOnly]
    private static uint OriginalRumble(uint slot, nint vibration)
    {
        Restored.Add((slot, *(ushort*)vibration, *(ushort*)(vibration + 2)));
        return 0;
    }

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        var errors = new List<Exception>();
        foreach (var run in new Action[]
        {
            () => CheckGuardAcquisition(check, plugin), () => CheckGuardEnableRollback(check, plugin),
            () => CheckGuardDispose(check, plugin), () => CheckCaptureDispose(check, plugin),
            () => CheckEffectConstructorRollback(check, plugin), () => CheckNestedEnemyDispose(check, plugin),
            () => { if (ProductionBoundaryChecks.Diagnostics(plugin)) CheckSourceObserverAcquisition(check, plugin); },
            () => { if (ProductionBoundaryChecks.Diagnostics(plugin)) CheckSourceObserverLifecycle(check, plugin); },
            () => CheckRotationObserverLifecycle(check, plugin), () => CheckMotionSessionAndGuard(check, plugin),
            () => CheckMotionFilterPermission(check, plugin), () => CheckFirstCameraFilter(check, plugin),
            () => CheckXInputDispose(check, plugin), () => CheckWorkerFinally(check, plugin),
            () => CheckPluginShutdown(check, plugin),
        })
            try { run(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Actual owner lifecycle failures.", errors);
    }

    private static ModuleBuilder CreateHookModule()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("OwnerLifecycleHooks"), AssemblyBuilderAccess.Run);
        // Hook<T>'s delegate arguments are private nested types in the plugin.
        // This managed test assembly needs access to form the exact closed type.
        var attribute = typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute).GetConstructor(new[] { typeof(string) })!;
        assembly.SetCustomAttribute(new CustomAttributeBuilder(attribute, new object[] { "DualSenseLighting" }));
        assembly.SetCustomAttribute(new CustomAttributeBuilder(attribute, new object[] { "Dalamud" }));
        return assembly.DefineDynamicModule("Hooks");
    }

    private static object Hook(Type hookBase, Action dispose, Action? enable = null, Action? disable = null)
    {
        if (!HookTypes.TryGetValue(hookBase, out var type))
        {
            var builder = HookModule.DefineType("ManagedHook" + HookTypes.Count, TypeAttributes.Public, hookBase);
            var enabled = builder.DefineField("Enabled", typeof(bool), FieldAttributes.Public);
            var enabledGetter = builder.DefineMethod("get_IsEnabled", MethodAttributes.Public | MethodAttributes.Virtual,
                typeof(bool), Type.EmptyTypes);
            var getterCode = enabledGetter.GetILGenerator(); getterCode.Emit(OpCodes.Ldarg_0);
            getterCode.Emit(OpCodes.Ldfld, enabled); getterCode.Emit(OpCodes.Ret);
            builder.DefineMethodOverride(enabledGetter, hookBase.GetProperty("IsEnabled")!.GetMethod!);
            foreach (var name in new[] { "Dispose", "Enable", "Disable" })
            {
                var callback = builder.DefineField(name + "Action", typeof(Action), FieldAttributes.Public);
                var method = builder.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Virtual,
                    typeof(void), Type.EmptyTypes);
                var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, callback);
                il.Emit(OpCodes.Callvirt, typeof(Action).GetMethod("Invoke")!);
                if (name != "Dispose") { il.Emit(OpCodes.Ldarg_0); il.Emit(name == "Enable" ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stfld, enabled); }
                il.Emit(OpCodes.Ret);
                builder.DefineMethodOverride(method, hookBase.GetMethod(name)!);
            }
            // No constructor runs in these fixtures. Supply a valid IL body for type creation only.
            var constructor = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            var code = constructor.GetILGenerator(); code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Ldc_I4_0);
            code.Emit(OpCodes.Conv_I); code.Emit(OpCodes.Call, hookBase.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0]); code.Emit(OpCodes.Ret);
            type = builder.CreateType()!; HookTypes.Add(hookBase, type);
        }
        var hook = RuntimeHelpers.GetUninitializedObject(type);
        GC.SuppressFinalize(hook); // The fixture never acquired a native hook.
        Set(type, hook, "DisposeAction", dispose);
        Set(type, hook, "EnableAction", enable ?? (() => { }));
        Set(type, hook, "DisableAction", disable ?? (() => { }));
        return hook;
    }

    private static object Interop(Type api, Func<Type, object> factory)
    {
        // DispatchProxy's generated generic HookFromSymbol<T> drops the SDK's
        // Delegate constraint. Implement this interface with copied constraints.
        var builder = HookModule.DefineType("Interop" + Guid.NewGuid().ToString("N"), TypeAttributes.Public);
        builder.AddInterfaceImplementation(api);
        var create = builder.DefineField("Factory", typeof(Func<Type, object>), FieldAttributes.Public);
        var methods = api.GetInterfaces().Append(api).SelectMany(x => x.GetMethods()).Distinct().ToArray();
        for (var index = 0; index < methods.Length; index++)
        {
            var source = methods[index];
            var method = builder.DefineMethod("Method" + index,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot);
            var parameters = source.GetGenericArguments();
            var generics = parameters.Length == 0 ? Array.Empty<GenericTypeParameterBuilder>() :
                method.DefineGenericParameters(parameters.Select(x => x.Name).ToArray());
            Type Map(Type value)
            {
                if (value.IsGenericParameter) return generics[Array.IndexOf(parameters, value)];
                if (value.IsByRef) return Map(value.GetElementType()!).MakeByRefType();
                if (value.IsPointer) return Map(value.GetElementType()!).MakePointerType();
                if (value.IsArray) return value.GetArrayRank() == 1 ? Map(value.GetElementType()!).MakeArrayType() :
                    Map(value.GetElementType()!).MakeArrayType(value.GetArrayRank());
                return value.IsGenericType ? value.GetGenericTypeDefinition().MakeGenericType(value.GetGenericArguments().Select(Map).ToArray()) : value;
            }
            for (var i = 0; i < parameters.Length; i++)
            {
                generics[i].SetGenericParameterAttributes(parameters[i].GenericParameterAttributes);
                var constraints = parameters[i].GetGenericParameterConstraints().Select(Map).ToArray();
                var parent = constraints.FirstOrDefault(x => !x.IsInterface);
                if (parent != null) generics[i].SetBaseTypeConstraint(parent);
                generics[i].SetInterfaceConstraints(constraints.Where(x => x.IsInterface).ToArray());
            }
            var result = Map(source.ReturnType);
            method.SetReturnType(result);
            method.SetParameters(source.GetParameters().Select(x => Map(x.ParameterType)).ToArray());
            var code = method.GetILGenerator();
            if (source.Name is "HookFromSymbol" or "HookFromAddress")
            {
                code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Ldfld, create);
                code.Emit(OpCodes.Ldtoken, result); code.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
                code.Emit(OpCodes.Callvirt, typeof(Func<Type, object>).GetMethod("Invoke")!);
                code.Emit(OpCodes.Castclass, result); code.Emit(OpCodes.Ret);
            }
            else { code.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!); code.Emit(OpCodes.Throw); }
            builder.DefineMethodOverride(method, source);
        }
        var implementation = builder.CreateType()!;
        var proxy = RuntimeHelpers.GetUninitializedObject(implementation);
        Set(implementation, proxy, "Factory", factory);
        return proxy;
    }

    private static object Proxy(Type type, Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = DispatchProxy.Create(type, typeof(ServiceProxy));
        ((ServiceProxy)proxy).Handler = handler;
        return proxy;
    }

    private static object? Default(MethodInfo method) => method.ReturnType == typeof(void) || !method.ReturnType.IsValueType
        ? null : Activator.CreateInstance(method.ReturnType);
    private static Type PluginType(Assembly plugin, string name) => plugin.GetType("DualSenseLighting." + name, true)!;
    private static FieldInfo Field(Type type, string name) => type.GetField(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new MissingFieldException(type.FullName, name);
    private static void Set(Type type, object target, string name, object? value) => Field(type, name).SetValue(target, value);
    private static object? Invoke(Type type, object target, string name, params object?[] args)
    {
        try { return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error.InnerException); throw; }
    }
    private static object ConstantGetter(Type valueType, object? value) =>
        Expression.Lambda(typeof(Func<>).MakeGenericType(valueType), Expression.Constant(value, valueType)).Compile();
    private static object New(Type type, params object?[] args) => Activator.CreateInstance(type,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, args, null)!;
    private static List<Exception> Flatten(Exception error) => error is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(Flatten).ToList() : new List<Exception> { error };

    private static void CheckGuardAcquisition(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Windows.GameOutputGuard");
        var interopType = type.GetConstructors()[0].GetParameters()[0].ParameterType;
        var sentinel = new InvalidOperationException("second hook acquisition");
        var acquired = 0; var disposed = 0;
        var interop = Interop(interopType, hookType =>
        {
            if (++acquired == 2) throw sentinel;
            return Hook(hookType, () => { disposed++; throw new InvalidOperationException("rollback hook disposal"); });
        });
        var constructor = type.GetConstructors()[0];
        var parameters = constructor.GetParameters();
        var lighting = PluginType(plugin, "Core.LightingSettings").GetProperty("Default")!.GetValue(null);
        var limitBreak = PluginType(plugin, "Core.LimitBreakState").GetProperty("Empty")!.GetValue(null);
        var values = new object?[] { interop, ConstantGetter(parameters[1].ParameterType.GenericTypeArguments[0], lighting),
            (Func<double>)(() => 0), (Func<float>)(() => 0), ConstantGetter(parameters[4].ParameterType.GenericTypeArguments[0], limitBreak), false };
        try { constructor.Invoke(values); check(false, "Guard constructor swallowed hook acquisition failure"); }
        catch (TargetInvocationException error)
        { check(ReferenceEquals(error.InnerException, sentinel) && acquired == 2 && disposed == 1,
            "Guard constructor failed to dispose its acquired hook or replaced the original acquisition error: acquired=" + acquired + ", disposed=" + disposed + ", error=" + error.InnerException); }
    }

    private static void CheckGuardEnableRollback(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Windows.GameOutputGuard");
        var constructor = type.GetConstructors()[0];
        var order = new List<string>(); var acquired = 0;
        var original = new InvalidOperationException("enable output hook");
        var interop = Interop(constructor.GetParameters()[0].ParameterType, hookType =>
        {
            var name = ++acquired == 1 ? "write" : "output";
            return Hook(hookType, () => { order.Add("dispose " + name); if (name == "output") throw new InvalidOperationException("dispose output"); },
                () => { order.Add("enable " + name); if (name == "output") throw original; },
                () => { order.Add("disable " + name); throw new InvalidOperationException("disable " + name); });
        });
        var lighting = PluginType(plugin, "Core.LightingSettings"); var limitBreak = PluginType(plugin, "Core.LimitBreakState");
        try
        {
            constructor.Invoke(new object[] { interop, ConstantGetter(lighting, lighting.GetProperty("Default")!.GetValue(null)),
                (Func<double>)(() => 0), (Func<float>)(() => 0), ConstantGetter(limitBreak, limitBreak.GetProperty("Empty")!.GetValue(null)), true });
            check(false, "Guard constructor swallowed Enable failure");
        }
        catch (TargetInvocationException error)
        {
            check(ReferenceEquals(error.InnerException, original) && order.SequenceEqual(new[]
                { "enable write", "enable output", "disable write", "dispose output", "dispose write" }),
                "Guard constructor lost Enable failure or skipped rollback after Disable/Dispose also failed");
        }
    }

    private static void CheckGuardDispose(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Windows.GameOutputGuard");
        var guard = RuntimeHelpers.GetUninitializedObject(type);
        var order = new List<string>(); var failure = new InvalidOperationException("output hook dispose");
        Set(type, guard, "setOutput", Hook(Field(type, "setOutput").FieldType, () => { order.Add("output"); throw failure; }));
        Set(type, guard, "writeFile", Hook(Field(type, "writeFile").FieldType, () => order.Add("write")));
        try { Invoke(type, guard, "Dispose"); check(false, "Guard disposal hid failed hook cleanup"); }
        catch (AggregateException error)
        { check(order.SequenceEqual(new[] { "output", "write" }) && Flatten(error).Contains(failure),
            "Actual guard disposal skipped the write hook after output hook failure"); }
    }

    private static object Capture(Assembly plugin, Action render, Action? animation = null, Action? confirmation = null)
    {
        var type = PluginType(plugin, "Windows.SfxAudioCapture");
        var capture = RuntimeHelpers.GetUninitializedObject(type);
        Set(type, capture, "<Loudness>k__BackingField", New(PluginType(plugin, "Core.SfxLoudness")));
        Set(type, capture, "render", Hook(Field(type, "render").FieldType, render));
        if (animation != null) Set(type, capture, "animation", Hook(Field(type, "animation").FieldType, animation));
        if (confirmation != null) Set(type, capture, "confirmationRender", Hook(Field(type, "confirmationRender").FieldType, confirmation));
        return capture;
    }

    private static void CheckCaptureDispose(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Windows.SfxAudioCapture");
        var hooks = new[] { "confirmationRender", "animation", "acquire", "resolve", "characterSound", "clipUpdate", "getTimeline", "render" };
        foreach (var failed in hooks)
        {
            var order = new List<string>(); var failure = new InvalidOperationException(failed);
            var capture = Capture(plugin, () => { });
            foreach (var name in hooks)
                Set(type, capture, name, Hook(Field(type, name).FieldType,
                    () => { order.Add(name); if (name == failed) throw failure; }));
            if (ProductionBoundaryChecks.Diagnostics(plugin)) Set(type, capture, "tracing", 1);
            try { Invoke(type, capture, "Dispose"); check(false, "Capture disposal hid hook cleanup failure"); }
            catch (AggregateException error)
            { check(order.SequenceEqual(hooks) && Flatten(error).Contains(failure),
                "Actual capture disposal skipped later hooks after " + failed + " failed"); }
            if (ProductionBoundaryChecks.Diagnostics(plugin))
                check((int)Field(type, "tracing").GetValue(capture)! == 0, "Capture retained tracing during disposal");
            Invoke(type, capture, "Dispose");
            check(order.Count == hooks.Length, "Repeated actual capture disposal repeated native hook cleanup");
        }
    }

    private static void CheckEffectConstructorRollback(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Windows.EnemyEffectObserver");
        var constructor = type.GetConstructors()[0];
        var module = Marshal.AllocHGlobal(256);
        try
        {
            new Span<byte>((void*)module, 256).Clear();
            // This observer validates two relative table addresses; both point
            // into this owned buffer. No executable/game memory is inspected.
            var metadata = RuntimeHelpers.GetUninitializedObject(typeof(ProcessModule));
            Set(typeof(ProcessModule), metadata, "<BaseAddress>k__BackingField", module);
            Set(typeof(ProcessModule), metadata, "<ModuleMemorySize>k__BackingField", 256);
            var scanner = Proxy(constructor.GetParameters()[1].ParameterType, (method, args) => method.Name switch
            {
                "get_Module" => metadata, "get_SearchBase" => module, "ScanAllText" => new[] { module }, _ => Default(method),
            });
            foreach (var cleanupFails in new[] { false, true })
            {
                var enabled = new InvalidOperationException("effect Enable failure");
                var disposed = new InvalidOperationException("effect rollback Dispose failure");
                var attempts = 0;
                var interop = Interop(constructor.GetParameters()[0].ParameterType,
                    hookType => Hook(hookType, () => { attempts++; if (cleanupFails) throw disposed; }, () => throw enabled));
                try { constructor.Invoke(new[] { interop, scanner }); check(false, "Effect constructor swallowed hook Enable failure"); }
                catch (TargetInvocationException error)
                {
                    var failures = Flatten(error.InnerException!);
                    check(attempts == 1 && (cleanupFails
                        ? failures.Count == 2 && ReferenceEquals(failures[0], enabled) && failures.Contains(disposed)
                        : ReferenceEquals(error.InnerException, enabled)),
                        "Effect constructor rollback lost the original Enable failure; actual=" + error.InnerException);
                }
            }
        }
        finally { Marshal.FreeHGlobal(module); }
    }

    private static void CheckSourceObserverAcquisition(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Windows.ScreenEffectSourceObserver");
        var constructor = type.GetConstructors()[0];
        var module = Marshal.AllocHGlobal(0x1000);
        try
        {
            new Span<byte>((void*)module, 0x1000).Clear();
            // Owned non-executable bytes satisfy the observer's structural
            // guards; this fixture tests acquisition rollback, not game ABI.
            void Put(int offset, byte[] bytes) => Marshal.Copy(bytes, 0, module + offset, bytes.Length);
            Marshal.WriteInt32(module + 0x3A, 0x800 - 0x3E);
            Put(0x113, new byte[] { 0xF3, 0x0F, 0x10, 0x8F, 0xC8, 1, 0, 0 });
            Marshal.WriteByte(module + 0x11B, 0xE8);
            Marshal.WriteInt32(module + 0x11C, 0x200 - 0x120);
            Put(0x220, new byte[] { 0x48, 0x8B, 0x87, 0x28, 2, 0, 0 });
            Put(0x240, new byte[] { 0x80, 0x7B, 4, 6 });
            Put(0x260, new byte[] { 0x48, 0x8B, 0x88, 0x90, 0x14, 0, 0 });
            Put(0x280, new byte[] { 0x0F, 0x2F, 0xB1, 0xC0, 1, 0, 0 });
            Put(0x2A0, new byte[] { 0xF3, 0x0F, 0x11, 0x89, 0xD8, 1, 0, 0 });
            Marshal.WriteIntPtr(module + 0x8F8, module + 0xA00);
            Put(0xA00, new byte[] { 0xF3, 0x0F, 0x10, 0x91, 0xCC, 1, 0, 0,
                0xF3, 0x0F, 0x10, 0x89, 0xC8, 1, 0, 0, 0xE9 });
            Marshal.WriteInt32(module + 0xA11, 0x200 - 0xA15);
            var metadata = RuntimeHelpers.GetUninitializedObject(typeof(ProcessModule));
            Set(typeof(ProcessModule), metadata, "<BaseAddress>k__BackingField", module);
            Set(typeof(ProcessModule), metadata, "<ModuleMemorySize>k__BackingField", 0x1000);
            var signature = (string)type.GetField("ConstructorSignature", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
            var scanner = Proxy(constructor.GetParameters()[1].ParameterType, (method, args) => method.Name switch
            {
                "get_Module" => metadata, "get_SearchBase" => module,
                "ScanAllText" => new[] { Equals(args![0], signature) ? module : module + 0x200 }, _ => Default(method),
            });
            foreach (var cleanupFails in new[] { false, true })
            {
                var failure = new InvalidOperationException("source second hook acquisition");
                var cleanup = new InvalidOperationException("source acquired hook rollback");
                var acquired = 0; var disposed = 0;
                var interop = Interop(constructor.GetParameters()[0].ParameterType, hookType =>
                {
                    if (++acquired == 2) throw failure;
                    return Hook(hookType, () => { disposed++; if (cleanupFails) throw cleanup; });
                });
                try { constructor.Invoke(new[] { interop, scanner }); check(false, "Source observer swallowed acquisition failure"); }
                catch (TargetInvocationException error)
                {
                    var errors = Flatten(error.InnerException!);
                    check(acquired == 2 && disposed == 1 && (cleanupFails
                        ? errors.Count == 2 && errors.Contains(failure) && errors.Contains(cleanup)
                        : ReferenceEquals(error.InnerException, failure)),
                        "Source observer lost acquisition/rollback failures or skipped its first acquired hook");
                }
            }
        }
        finally { Marshal.FreeHGlobal(module); }
    }

    private static void CheckSourceObserverLifecycle(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Windows.ScreenEffectSourceObserver");
        foreach (var failedEnable in new[] { "constructor", "sampler" })
        {
            var owner = RuntimeHelpers.GetUninitializedObject(type); var order = new List<string>();
            foreach (var name in new[] { "constructor", "sampler" })
                Set(type, owner, name, Hook(Field(type, name).FieldType, () => order.Add("dispose " + name),
                    () => { order.Add("enable " + name); if (name == failedEnable) throw new InvalidOperationException(name + " enable"); },
                    () => order.Add("disable " + name)));
            Invoke(type, owner, "Start", 0L); Invoke(type, owner, "SetActive", true);
            check((int)Field(type, "enabled").GetValue(owner)! == 0 && (int)Field(type, "faulted").GetValue(owner)! == 1 &&
                order.Contains("enable " + failedEnable) && (failedEnable != "sampler" || order.Contains("disable constructor")),
                "Source Enable failure retained capture or skipped the already-enabled constructor cleanup");
            Invoke(type, owner, "Dispose");
            check(order.Contains("dispose sampler") && order.Contains("dispose constructor"),
                "Source Enable failure prevented later disposal of both acquired hooks");
        }
        var instance = RuntimeHelpers.GetUninitializedObject(type); var disabled = new List<string>();
        var failDisable = true;
        foreach (var name in new[] { "constructor", "sampler" })
            Set(type, instance, name, Hook(Field(type, name).FieldType, () => { disabled.Add("dispose " + name);
                if (name == "sampler") throw new InvalidOperationException("sample disposal"); },
                () => { }, () => { disabled.Add(name); if (failDisable) throw new InvalidOperationException(name + " disable"); }));
        Invoke(type, instance, "Start", 0L); Invoke(type, instance, "SetActive", true);
        Invoke(type, instance, "SetActive", false);
        check(disabled.Contains("sampler") && disabled.Contains("constructor") && (int)Field(type, "enabled").GetValue(instance)! == 0,
            "A failed source-hook disable skipped the other hook or retained active capture");
        failDisable = false; Invoke(type, instance, "SetActive", false);
        var hooksDisabled = true;
        foreach (var name in new[] { "constructor", "sampler" })
        {
            var hook = Field(type, name).GetValue(instance)!;
            hooksDisabled &= !(bool)hook.GetType().GetProperty("IsEnabled")!.GetValue(hook)!;
        }
        check(hooksDisabled,
            "Source hook disable failure was not retried on the next owner update");
        var recording = Field(type, "recording").GetValue(instance)!;
        try { Invoke(type, instance, "Dispose"); check(false, "Source unload hid sample hook cleanup failure"); }
        catch (AggregateException error)
        {
            check(Flatten(error).Any(x => x is InvalidOperationException && x.Message == "sample disposal"),
                "Source cleanup aggregate lost its sample disposal failure");
            var snapshot = Invoke(recording.GetType(), recording, "Snapshot")!;
            check(disabled.Contains("dispose sampler") && disabled.Contains("dispose constructor") &&
                (bool)snapshot.GetType().GetProperty("Retired")!.GetValue(snapshot)!,
                "Source unload skipped its other hook or failed to retire the recording after cleanup failure");
        }
    }

    private static void CheckRotationObserverLifecycle(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Windows.SecondaryMotionObserver");
        foreach (var failedEnable in new[] { "producer", "rotationSetter", "positionSetter", "reset" })
        {
            var owner = RuntimeHelpers.GetUninitializedObject(type); var order = new List<string>();
            foreach (var name in new[] { "producer", "rotationSetter", "positionSetter", "reset" })
                Set(type, owner, name, Hook(Field(type, name).FieldType, () => order.Add("dispose " + name),
                    () => { order.Add("enable " + name); if (name == failedEnable) throw new InvalidOperationException(name); },
                    () => order.Add("disable " + name)));
            Invoke(type, owner, "SetActive", true);
            check((int)Field(type, "enabled").GetValue(owner)! == 0 && (int)Field(type, "faulted").GetValue(owner)! == 1 &&
                order.Contains("enable " + failedEnable) && (failedEnable != "producer" || order.Contains("disable rotationSetter")) &&
                (failedEnable != "producer" && failedEnable != "rotationSetter" || order.Contains("disable positionSetter")) &&
                (failedEnable == "reset" || order.Contains("disable reset")),
                "Rotation enable failure retained capture or skipped already-enabled hook rollback");
            Invoke(type, owner, "Dispose");
            check(order.Contains("dispose rotationSetter") && order.Contains("dispose producer") && order.Contains("dispose reset") && order.Contains("dispose positionSetter"),
                "Rotation enable failure prevented all four hook disposals");
        }
        var instance = RuntimeHelpers.GetUninitializedObject(type); var operations = new List<string>();
        var failDisable = true;
        foreach (var name in new[] { "producer", "rotationSetter", "positionSetter", "reset" })
            Set(type, instance, name, Hook(Field(type, name).FieldType,
                () => { operations.Add("dispose " + name); throw new InvalidOperationException(name + " dispose"); },
                () => { }, () => { operations.Add("disable " + name); if (failDisable) throw new InvalidOperationException(name); }));
        Invoke(type, instance, "SetActive", true); Invoke(type, instance, "SetActive", false);
        check(operations.Contains("disable producer") && operations.Contains("disable rotationSetter") && operations.Contains("disable reset") && operations.Contains("disable positionSetter") &&
            (int)Field(type, "enabled").GetValue(instance)! == 0,
            "Rotation disable failure skipped another hook or retained active capture");
        failDisable = false; Invoke(type, instance, "SetActive", false);
        check(new[] { "producer", "rotationSetter", "positionSetter", "reset" }.All(name =>
            !(bool)Field(type, name).GetValue(instance)!.GetType().GetProperty("IsEnabled")!.GetValue(Field(type, name).GetValue(instance))!),
            "Rotation disable failure was not retried");
        try { Invoke(type, instance, "Dispose"); check(false, "Rotation disposal hid cleanup failures"); }
        catch (AggregateException error)
        {
            var errors = Flatten(error);
            check(errors.Count == 4 && errors.Any(x => x.Message == "producer dispose") && errors.Any(x => x.Message == "rotationSetter dispose") &&
                errors.Any(x => x.Message == "reset dispose") && errors.Any(x => x.Message == "positionSetter dispose") &&
                operations.Contains("dispose producer") && operations.Contains("dispose rotationSetter") && operations.Contains("dispose reset") && operations.Contains("dispose positionSetter"),
                "Rotation disposal skipped a hook or lost cleanup failures");
        }
    }

    private static void CheckMotionSessionAndGuard(Action<bool, string> check, Assembly plugin)
    {
        if (ProductionBoundaryChecks.Diagnostics(plugin))
        {
            var recordingType = PluginType(plugin, "Core.ScreenEffectSourceRecording");
            var sessionType = PluginType(plugin, "Windows.SecondaryMotionSession");
            var sampleType = PluginType(plugin, "Core.SecondaryVectorSample");
            var recording = New(recordingType, 0L);
            var session = New(sessionType, recording, null);
            var sample = New(sampleType, 100L, 200L, 300L, 0, true, .1f, .2f, .3f);
            int Count()
            {
                var snapshot = Invoke(recordingType, recording, "Snapshot")!;
                var events = snapshot.GetType().GetProperty("Events")!.GetValue(snapshot)!;
                return (int)events.GetType().GetProperty("Length")!.GetValue(events)!;
            }
            Invoke(sessionType, session, "Observe", sample, true, 1L);
            check(Count() == 1, "Active motion session did not record its position observation");
            Invoke(sessionType, session, "Retire", 2L);
            var afterRetire = Count();
            Invoke(sessionType, session, "Observe", sample, true, 3L);
            Invoke(sessionType, session, "Reset", 100L, 200L, 300L, (uint)0, (uint)0, 4L);
            Invoke(sessionType, session, "Gap", 5L);
            Invoke(sessionType, session, "Retire", 6L);
            check(Count() == afterRetire, "Retired motion callbacks mutated their recording or repeated retirement");
            var next = New(sessionType, recording, null);
            Invoke(sessionType, next, "Observe", sample, false, 7L);
            check(Count() == afterRetire + 1, "Retiring an old motion session prevented the new session from recording");
            Invoke(sessionType, session, "Observe", sample, false, 8L);
            check(Count() == afterRetire + 1, "An old callback mutated recording after a new motion session was admitted");
        }
        else
        {
            var sessionType = PluginType(plugin, "Windows.SecondaryMotionSession");
            var session = New(sessionType, new object?[] { null });
            check((bool)sessionType.GetProperty("Active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!, "Production session did not activate");
            Invoke(sessionType, session, "Retire", 2L);
            Invoke(sessionType, session, "Retire", 3L);
            check(!(bool)sessionType.GetProperty("Active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!, "Production session retirement was not retained");
        }
        var guardType = PluginType(plugin, "Core.ScreenEffectFilterGuard");
        var kind = PluginType(plugin, "Core.ScreenEffectKind");
        var guard = New(guardType);
        var fresh = (long)PluginType(plugin, "Core.ScreenEffectHaptics").GetField("FreshTicks", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        float Apply(long clock, Vector3 primary, float scripted, int effects) =>
            (float)Invoke(guardType, guard, "Apply", .2f, primary, Vector3.Zero, scripted, Enum.ToObject(kind, effects), clock)!;
        for (var i = 0; i < 8; i++)
            check(Apply(fresh * (i + 1), Vector3.UnitX, 0, 1) == 1,
                "A long primary attack lost full feedback beyond the initial protection interval");
        check(Apply(fresh * 8 + fresh / 2, Vector3.Zero, 0, 1) == 1,
            "Primary zero crossing lost its recent-effect protection");
        check(Apply(fresh * 10, Vector3.Zero, 0, 1) == .2f,
            "Quiet primary protection did not expire back to the qualified candidate");
        check(Apply(fresh * 11, Vector3.Zero, 0, 3) == 1,
            "A non-dominant blur flag failed to protect full feedback alongside shake");
        check(Apply(fresh * 13, Vector3.Zero, .1f, 1) == 1 &&
            Apply(fresh * 13 + fresh / 2, Vector3.Zero, 0, 1) == 1,
            "Scripted shake or its zero crossing lost protection");
        check(Apply(fresh * 15, Vector3.Zero, 0, 1) == .2f,
            "Scripted protection did not expire after a quiet interval");
    }

    private static void CheckMotionFilterPermission(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Windows.GameScreenEffects");
        var observerType = PluginType(plugin, "Windows.SecondaryMotionObserver");
        var outputType = PluginType(plugin, "Windows.NativeHapticOutput");
        var settingsType = PluginType(plugin, "Core.HapticSettings");
        var constructor = settingsType.GetConstructors().Single(x => x.GetParameters().Length > 1);
        var settings = constructor.Invoke(constructor.GetParameters().Select(x => x.DefaultValue).ToArray());
        settingsType.GetProperty("ScreenEffects")!.SetValue(settings, true);
        var output = RuntimeHelpers.GetUninitializedObject(outputType);
        Set(outputType, output, "screenSettings", settings);
        var owner = RuntimeHelpers.GetUninitializedObject(type);
        Set(type, owner, "output", output);
        var observer = RuntimeHelpers.GetUninitializedObject(observerType);
        foreach (var name in new[] { "producer", "rotationSetter", "positionSetter", "reset" })
            Set(observerType, observer, name, Hook(Field(observerType, name).FieldType, () => { }, () => { }, () => { }));
        Set(type, owner, "motionObserver", observer);
        Invoke(type, owner, "UpdateMotion", true, true);
        var session = Field(type, "motionSession").GetValue(owner)!;
        var sessionType = session.GetType();
        check(Field(sessionType, "Filter").GetValue(session) != null &&
            (!ProductionBoundaryChecks.Diagnostics(plugin) || Field(sessionType, "Recording").GetValue(session) == null) && (int)Field(observerType, "enabled").GetValue(observer)! == 1,
            "Normal screen filtering required recording or failed to activate its fixed matcher");
        Invoke(type, owner, "UpdateMotion", true, false);
        check(Field(type, "motionSession").GetValue(owner) == null &&
            !(bool)sessionType.GetProperty("Active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)! &&
            (int)Field(observerType, "enabled").GetValue(observer)! == 0,
            "Background state retained active filter callbacks or a live session");
        Invoke(type, owner, "UpdateMotion", true, true);
        check(Field(type, "motionSession").GetValue(owner) != null,
            "Foreground return failed to resume fixed-pattern observation");
        Invoke(type, owner, "UpdateMotion", false, true);
        check(Field(type, "motionSession").GetValue(owner) == null && (int)Field(observerType, "enabled").GetValue(observer)! == 0,
            "Invalid character/duty permission kept filter observation running");
    }

    private static void CheckFirstCameraFilter(Action<bool, string> check, Assembly plugin)
    {
        // Exercise the compiled camera reader with owned data buffers. No game
        // process, executable memory, OS hooks or physical controller is used.
        var allocations = new List<nint>();
        nint Buffer(int size)
        {
            var memory = Marshal.AllocHGlobal(size); allocations.Add(memory);
            new Span<byte>((void*)memory, size).Clear(); return memory;
        }
        try
        {
            var module = Buffer(256); var post = Buffer(0x4800); var camera = Buffer(256);
            var scene = Buffer(8); var nextScene = Buffer(8);
            *(nint*)post = *(nint*)camera = *(nint*)scene = *(nint*)nextScene = module;
            var postGlobal = Buffer(8); var cameraGlobal = Buffer(8);
            *(nint*)postGlobal = post; *(nint*)cameraGlobal = camera;
            *(nint*)(camera + 0x58) = scene;
            var type = PluginType(plugin, "Windows.GameScreenEffects");
            var owner = RuntimeHelpers.GetUninitializedObject(type);
            var contextType = type.GetNestedType("CaptureContext", BindingFlags.NonPublic)!;
            var context = New(contextType);
            var filterType = PluginType(plugin, "Core.KnownMotionFilter");
            var filter = New(filterType, Stopwatch.Frequency);
            var outputType = PluginType(plugin, "Windows.NativeHapticOutput");
            var output = RuntimeHelpers.GetUninitializedObject(outputType);
            var settingsType = PluginType(plugin, "Core.HapticSettings");
            var constructor = settingsType.GetConstructors().Single(x => x.GetParameters().Length > 1);
            var settings = constructor.Invoke(constructor.GetParameters().Select(x => x.DefaultValue).ToArray());
            Set(outputType, output, "screenSettings", settings);
            Set(type, owner, "output", output); Set(type, owner, "context", context);
            Set(type, owner, "moduleStart", module); Set(type, owner, "moduleEnd", module + 256);
            Set(type, owner, "postGlobal", postGlobal); Set(type, owner, "cameraGlobal", cameraGlobal);
            Set(type, owner, "motionFilter", filter);
            var sessionType = PluginType(plugin, "Windows.SecondaryMotionSession");
            Set(type, owner, "motionSession", ProductionBoundaryChecks.Diagnostics(plugin)
                ? New(sessionType, null, filter) : New(sessionType, filter));
            Set(type, owner, "motionObserver", RuntimeHelpers.GetUninitializedObject(PluginType(plugin, "Windows.SecondaryMotionObserver")));
            // Warm the reader before measuring a fresh epoch's first data frame.
            Invoke(type, owner, "Read", camera, context);
            var readMethod = type.GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var directRead = new System.Reflection.Emit.DynamicMethod("ReadQuietCamera", typeof(object), new[] { typeof(nint), typeof(object), typeof(object) }, type, true);
            var il = directRead.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1); il.Emit(System.Reflection.Emit.OpCodes.Castclass, type);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0); il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, contextType); il.Emit(System.Reflection.Emit.OpCodes.Call, readMethod);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            var read = directRead.CreateDelegate<Func<nint, object, object, object?>>();
            var diagnostics = ProductionBoundaryChecks.Diagnostics(plugin);
            var quiet = read(camera, owner, context);
            check(diagnostics ? quiet != null && (float)quiet.GetType().GetProperty("Level")!.GetValue(quiet)! == 0 : quiet == null,
                "Quiet camera publication changed development recording or retained a production sample");
            if (!diagnostics)
            {
                Action quietRead = () => _ = read(camera, owner, context);
                check(AllocationChecks.MeasureAllocations(quietRead, warmup: 2000, iterations: 1000) == 0,
                    "The compiled production camera reader still allocates on quiet frames");
            }
            context = New(contextType); Set(type, owner, "context", context);
            var profiles = (Array)Field(filterType, "patterns").GetValue(filter)!;
            var profile = profiles.GetValue(0)!;
            var rotation = (Vector3)Invoke(profile.GetType(), profile, "At", .237)!;
            var values = (float*)(camera + 0x40);
            values[0] = rotation.X; values[1] = rotation.Y; values[2] = rotation.Z;
            var sample = New(PluginType(plugin, "Core.SecondaryVectorSample"), 1L, 101L, 201L, 0, true,
                rotation.X, rotation.Y, rotation.Z);
            Invoke(filterType, filter, "Observe", sample, false, Stopwatch.GetTimestamp());
            var first = Invoke(type, owner, "Read", camera, context)!;
            check((float)first.GetType().GetProperty("Level")!.GetValue(first)! > 0 &&
                (float)first.GetType().GetProperty("AmplitudeMultiplier")!.GetValue(first)! == 0,
                "The first camera frame discarded fresh source observations and leaked vehicle vibration");
            *(nint*)(camera + 0x58) = nextScene;
            var changed = Invoke(type, owner, "Read", camera, context)!;
            check((float)changed.GetType().GetProperty("AmplitudeMultiplier")!.GetValue(changed)! == 1,
                "A genuine camera replacement retained attribution from the previous scene camera");
        }
        finally { foreach (var memory in allocations) Marshal.FreeHGlobal(memory); }
    }

    private static void CheckNestedEnemyDispose(Action<bool, string> check, Assembly plugin)
    {
        var captureType = PluginType(plugin, "Windows.SfxAudioCapture");
        var enemyType = PluginType(plugin, "Windows.EnemySkillFilter");
        var effectType = PluginType(plugin, "Windows.EnemyEffectObserver");
        var effects = RuntimeHelpers.GetUninitializedObject(effectType);
        var order = new List<string>(); var failure = new InvalidOperationException("nested effect hook disposal");
        Set(effectType, effects, "constructor", Hook(Field(effectType, "constructor").FieldType,
            () => { order.Add("effect"); throw failure; }));
        var enemy = RuntimeHelpers.GetUninitializedObject(enemyType);
        Set(enemyType, enemy, "effects", effects);
        Set(enemyType, enemy, "receive", Hook(Field(enemyType, "receive").FieldType, () => order.Add("receive")));
        var capture = Capture(plugin, () => order.Add("render"));
        Set(captureType, capture, "enemySkills", enemy);
        try { Invoke(captureType, capture, "Dispose"); check(false, "Capture hid nested enemy hook failure"); }
        catch (AggregateException error)
        {
            check(order.SequenceEqual(new[] { "effect", "receive", "render" }) && Flatten(error).Contains(failure),
                "Nested enemy effect disposal skipped receive hook; actual order=" + string.Join(",", order));
        }
        Invoke(captureType, capture, "Dispose"); Invoke(enemyType, enemy, "Dispose");
        check(order.Count == 3, "Repeated capture/enemy cleanup repeated acquired native resources");
    }

    private static void CheckXInputDispose(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Windows.XInputRumble");
        var interopType = type.GetConstructors()[0].GetParameters()[0].ParameterType;
        var foregroundCalls = 0;
        var restoreFailure = new InvalidOperationException("restore foreground check");
        var xinput = New(type, Interop(interopType, _ => throw new NotSupportedException()),
            (Func<bool>)(() => { if (++foregroundCalls == 2) throw restoreFailure; return true; }));
        var hooks = (IList)Field(type, "hooks").GetValue(xinput)!;
        var hookType = Field(type, "hooks").FieldType.GenericTypeArguments[0];
        var order = new List<int>();
        var first = Hook(hookType, () => { order.Add(1); throw new InvalidOperationException("first hook"); });
        Set(hookType, first, "address", (nint)(delegate* unmanaged<uint, nint, uint>)&OriginalRumble);
        Set(hookType, first, "<IsDisposed>k__BackingField", true);
        hooks.Add(first); hooks.Add(Hook(hookType, () => order.Add(2)));
        var game = (Array)Field(type, "game").GetValue(xinput)!;
        var extra = (Array)Field(type, "plugin").GetValue(xinput)!;
        var motors = game.GetType().GetElementType()!;
        for (var i = 0; i < 4; i++)
        { game.SetValue(New(motors, (ushort)(10 + i), (ushort)(20 + i)), i); extra.SetValue(New(motors, (ushort)1000, (ushort)2000), i); }
        var module = NativeLibrary.Load(System.IO.Path.Combine(Environment.SystemDirectory, "xinput1_4.dll"));
        Set(type, xinput, "module", module);
        Restored.Clear();
        try { Invoke(type, xinput, "Dispose"); check(false, "XInput disposal hid failed hook release"); }
        catch (AggregateException error)
        { check(Flatten(error).Contains(restoreFailure) && order.SequenceEqual(new[] { 1, 2 }) && hooks.Count == 0 &&
            (nint)Field(type, "module").GetValue(xinput)! == 0, "XInput skipped another hook or retained the hook collection after disposal failure"); }
        check(Restored.Select(x => x.Slot).SequenceEqual(new uint[] { 0, 2, 3 }) && Restored.All(x => x.Low == 10 + x.Slot && x.High == 20 + x.Slot),
            "XInput disposal failed to restore the game's motors on later slots after an earlier restoration exception");
        Invoke(type, xinput, "Dispose");
        check(order.Count == 2 && Restored.Count == 3, "Repeated actual XInput disposal repeated output or hook actions");
    }

    private static object Rumble(Assembly plugin, Action stop)
    {
        var type = PluginType(plugin, "Core.RumbleSession");
        var deviceType = PluginType(plugin, "Core.IRumbleDevices");
        var devices = Proxy(deviceType, (method, args) => { if (method.Name == "SetVibration") { stop(); return true; } return Default(method); });
        var rumble = New(type, devices, (Func<bool>)(() => false));
        Set(type, rumble, "slot", 0);
        Set(type, rumble, "sent", New(PluginType(plugin, "Core.RumbleMotors"), (ushort)100, (ushort)100));
        return rumble;
    }

    private static object Session(Assembly plugin, Action? write = null, Action? close = null)
    {
        var type = PluginType(plugin, "Core.LightingSession");
        var devicesType = PluginType(plugin, "Core.ILightingDevices");
        var devices = Proxy(devicesType, (method, args) => Default(method));
        var session = New(type, devices, (Func<bool>)(() => false), (Action<Exception>)(_ => { }));
        if (write != null)
        {
            var deviceType = PluginType(plugin, "Core.ILightingDevice");
            var device = Proxy(deviceType, (method, args) =>
            {
                if (method.Name == "get_ReportLength") return 48;
                if (method.Name == "WriteAsync") { write(); return Task.CompletedTask; }
                if (method.Name == "Dispose") { close!(); return null; }
                return Default(method);
            });
            ((IDictionary)Field(type, "open").GetValue(session)!).Add("owned", device);
            var states = (IDictionary)Field(type, "sent").GetValue(session)!;
            states.Add("owned", New(states.GetType().GenericTypeArguments[1]));
            var obligations = (IDictionary)Field(type, "owned").GetValue(session)!;
            var obligation = New(obligations.GetType().GenericTypeArguments[1]);
            Set(obligation.GetType(), obligation, "Lighting", true); obligations.Add("owned", obligation);
        }
        return session;
    }

    private static void CheckWorkerFinally(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Plugin"); var instance = RuntimeHelpers.GetUninitializedObject(type);
        var first = 0; var second = 0; var writes = 0; var closed = 0;
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Set(type, instance, "stop", stop); Set(type, instance, "log", QuietLog(plugin));
        Set(type, instance, "rumble", Rumble(plugin, () => { first++; throw new InvalidOperationException("rumble release"); }));
        Set(type, instance, "bluetoothRumble", Rumble(plugin, () => second++));
        Set(type, instance, "session", Session(plugin, () => writes++, () => closed++));
        Set(type, instance, "sfxLevel", 1f);
        ((Task)Invoke(type, instance, "RunWorker")!).GetAwaiter().GetResult();
        check(first == 1 && second == 1 && writes == 1 && closed == 1 && (float)Field(type, "sfxLevel").GetValue(instance)! == 0,
            "Actual worker finally skipped Bluetooth/device release or level reset after XInput release failed");
    }

    private static object QuietLog(Assembly plugin, Action<Exception>? report = null)
    {
        var type = Field(PluginType(plugin, "Plugin"), "log").FieldType;
        return Proxy(type, (method, args) => { if (args != null) foreach (var arg in args) if (arg is Exception error) report?.Invoke(error); return Default(method); });
    }

    private static void CheckPluginShutdown(Action<bool, string> check, Assembly plugin)
    {
        var type = PluginType(plugin, "Plugin"); var instance = RuntimeHelpers.GetUninitializedObject(type);
        var detached = new ManualResetEventSlim(); var fontDisposed = 0; var captureDisposed = 0; var saved = 0; var commandsRemoved = 0;
        var failures = new List<Exception>();
        var log = QuietLog(plugin, error => failures.AddRange(Flatten(error)));
        var framework = Proxy(Field(type, "framework").FieldType, (method, args) =>
        { if (method.Name == "remove_Update") { detached.Set(); throw new InvalidOperationException("event detach"); } return Default(method); });
        var uiBuilderReads = 0;
        var pi = Proxy(Field(type, "pluginInterface").FieldType, (method, args) =>
        { if (method.Name == "get_UiBuilder") { uiBuilderReads++; throw new InvalidOperationException("UI builder detach"); } return Default(method); });
        var commands = Proxy(Field(type, "commands").FieldType, (method, args) =>
        { if (method.Name == "RemoveHandler") { commandsRemoved++; return true; } return Default(method); });
        var config = New(PluginType(plugin, "Configuration"));
        Invoke(config.GetType(), config, "CompleteSetup", true, true);
        Set(type, instance, "settings", Invoke(config.GetType(), config, "Snapshot", true));
        Set(type, instance, "hapticSettings", Invoke(config.GetType(), config, "HapticsSnapshot", true));
        Set(type, instance, "configuration", config); Set(type, instance, "setupReady", true); Set(type, instance, "windowOpen", true);
        Set(type, instance, "framework", framework); Set(type, instance, "pluginInterface", pi); Set(type, instance, "commands", commands);
        Set(type, instance, "commandRegistered", true); Set(type, instance, "log", log);
        var stop = new CancellationTokenSource(); Set(type, instance, "stop", stop);
        var clock = Stopwatch.StartNew(); Set(type, instance, "clock", clock);
        var saves = New(PluginType(plugin, "Core.ConfigurationSaveScheduler"), (Action)(() => saved++), (Action<Exception>)(_ => { }));
        Invoke(saves.GetType(), saves, "MarkDirty", 1000d); Set(type, instance, "saves", saves);
        var captureType = PluginType(plugin, "Windows.SfxAudioCapture");
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "plugin-lifecycle-" + Guid.NewGuid());
        if (ProductionBoundaryChecks.Diagnostics(plugin))
        {
            var recorder = New(PluginType(plugin, "DiagnosticRecordingCoordinator"), directory,
                System.IO.Path.Combine(directory, "screen-haptics-timing.json"), config, log,
                ConstantGetter(captureType, null), "test", clock);
            Set(type, instance, "recording", recorder);
        }
        Set(type, instance, "session", Session(plugin));
        var guardType = PluginType(plugin, "Windows.GameOutputGuard"); var guard = RuntimeHelpers.GetUninitializedObject(guardType);
        var guardCalls = 0;
        Set(guardType, guard, "setOutput", Hook(Field(guardType, "setOutput").FieldType, () => { guardCalls++; throw new InvalidOperationException("guard cleanup"); }));
        Set(guardType, guard, "writeFile", Hook(Field(guardType, "writeFile").FieldType, () => guardCalls++));
        Set(type, instance, "guard", guard);
        var uiType = PluginType(plugin, "Ui.SettingsUi"); var ui = RuntimeHelpers.GetUninitializedObject(uiType);
        Set(uiType, ui, "uiBuilder", Proxy(Field(uiType, "uiBuilder").FieldType, (method, args) => Default(method)));
        Set(uiType, ui, "windowSystem", New(Field(uiType, "windowSystem").FieldType, "LifecycleCheck"));
        Set(uiType, ui, "uiFont", Proxy(Field(uiType, "uiFont").FieldType, (method, args) =>
        { if (method.Name == "Dispose") fontDisposed++; return Default(method); })); Set(type, instance, "ui", ui);
        var workerFailure = new InvalidOperationException("worker failure"); Set(type, instance, "worker", Task.FromException(workerFailure));
        var completion = New(typeof(TaskCompletionSource<>).MakeGenericType(captureType), TaskCreationOptions.RunContinuationsAsynchronously);
        Set(type, instance, "audioInitialization", completion.GetType().GetProperty("Task")!.GetValue(completion));
        var capture = Capture(plugin, () => captureDisposed++);
        var release = Task.Run(() =>
        {
            try
            {
                if (!detached.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Shutdown never detached the framework callback");
            }
            finally
            {
                // A broken detach path must fail this fixture, not strand the
                // actual shutdown waiting indefinitely for our owned task.
                completion.GetType().GetMethod("SetResult")!.Invoke(completion, new[] { capture });
            }
        });
        try
        {
            Invoke(type, instance, "Dispose"); release.GetAwaiter().GetResult();
            check(commandsRemoved == 1 && uiBuilderReads == 3 && captureDisposed == 1 && guardCalls == 2 && saved == 1 && fontDisposed == 1,
                "Actual Plugin shutdown skipped late observer disposal, hooks, dirty save or font after earlier failures");
            check(failures.Contains(workerFailure) && !((bool)Field(type, "windowOpen").GetValue(instance)!) &&
                !((bool)Field(type, "setupReady").GetValue(instance)!) &&
                !(bool)Field(type, "settings").GetValue(instance)!.GetType().GetProperty("Enabled")!.GetValue(Field(type, "settings").GetValue(instance))! &&
                !(bool)Field(type, "hapticSettings").GetValue(instance)!.GetType().GetProperty("NeedsAudio")!.GetValue(Field(type, "hapticSettings").GetValue(instance))!,
                "Actual Plugin shutdown lost the worker error or retained active published output");
            try { stop.Cancel(); check(false, "Plugin shutdown did not dispose its cancellation token source"); }
            catch (ObjectDisposedException) { }
            Invoke(type, instance, "Dispose");
            check(commandsRemoved == 1 && captureDisposed == 1 && guardCalls == 2 && saved == 1 && fontDisposed == 1,
                "Repeated actual Plugin disposal repeated acquired-resource cleanup");

            // A failed unsubscribe or previously captured event delegate may
            // start after teardown. Poison retired owners so any access fails;
            // this checks new callback entries, not callbacks already in flight.
            var retiredSettings = Field(type, "settings").GetValue(instance);
            var retiredHaptics = Field(type, "hapticSettings").GetValue(instance);
            var configurationBefore = System.Text.Json.JsonSerializer.Serialize(config, config.GetType());
            foreach (var owner in new[] { "ui", "saves", "guard", "chat" }) Set(type, instance, owner, null);
            if (ProductionBoundaryChecks.Diagnostics(plugin)) Set(type, instance, "recording", null);
            var callbackErrors = new List<string>();
            foreach (var callback in new (string Name, object?[] Arguments)[]
            {
                ("Open", Array.Empty<object?>()), ("Draw", Array.Empty<object?>()),
                ("Update", new object?[] { framework }),
                ("OnCommand", new object?[] { "/bcef", "lighting off" }),
            })
            {
                try { Invoke(type, instance, callback.Name, callback.Arguments); }
                catch (Exception error) { callbackErrors.Add(callback.Name + ": " + error.GetType().Name); }
            }
            check(callbackErrors.Count == 0,
                "Callbacks starting after Plugin disposal accessed retired owners: " + string.Join(", ", callbackErrors));
            check(!((bool)Field(type, "windowOpen").GetValue(instance)!) &&
                !((bool)Field(type, "setupReady").GetValue(instance)!) &&
                ReferenceEquals(retiredSettings, Field(type, "settings").GetValue(instance)) &&
                ReferenceEquals(retiredHaptics, Field(type, "hapticSettings").GetValue(instance)) &&
                System.Text.Json.JsonSerializer.Serialize(config, config.GetType()) == configurationBefore &&
                commandsRemoved == 1 && uiBuilderReads == 3 && captureDisposed == 1 && guardCalls == 2 && saved == 1 && fontDisposed == 1,
                "Callbacks starting after Plugin disposal reopened UI, changed configuration/publication or repeated owner cleanup");
        }
        finally { detached.Dispose(); stop.Dispose(); }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute
    {
        public string AssemblyName { get; } = assemblyName;
    }
}
