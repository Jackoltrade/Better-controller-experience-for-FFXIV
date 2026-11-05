// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Completed-disposal state at the actual plugin detour boundary. No OS/game
// hook is installed: restored native entry points are owned unmanaged sentinels.
internal static unsafe class HookOriginalLifecycleChecks
{
    private static int calls;
    private static nint lastFile;
    private static uint lastLength;
    [UnmanagedCallersOnly]
    private static int OriginalWrite(nint file, nint buffer, uint length, nint written, nint overlapped)
    { calls++; lastFile = file; lastLength = length; return 1; }
    [UnmanagedCallersOnly]
    private static byte OriginalOutput(nint file, nint buffer, uint length)
    { calls++; lastFile = file; lastLength = length; return 1; }

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        var type = plugin.GetType("DualSenseLighting.Windows.GameOutputGuard", true)!;
        var guard = RuntimeHelpers.GetUninitializedObject(type);
        var write = type.GetNestedType("WriteFileDelegate", BindingFlags.NonPublic)!;
        var output = type.GetNestedType("SetOutputDelegate", BindingFlags.NonPublic)!;
        Set(type, guard, "writeFile", DisposedHook(write,
            (nint)(delegate* unmanaged<nint, nint, uint, nint, nint, int>)&OriginalWrite));
        Set(type, guard, "setOutput", DisposedHook(output,
            (nint)(delegate* unmanaged<nint, nint, uint, byte>)&OriginalOutput));
        calls = 0;
        var first = Invoke(type, guard, "WriteFile", new object[] { (nint)123, (nint)0, (uint)48, (nint)0, (nint)0 });
        check(first && calls == 1 && lastFile == 123 && lastLength == 48,
            "An entered WriteFile detour did not forward the original exactly once after hook disposal");
        var second = Invoke(type, guard, "SetOutput", new object[] { (nint)456, (nint)0, (uint)78 });
        check(second && calls == 2 && lastFile == 456 && lastLength == 78,
            "An entered HID-output detour did not preserve the original after hook disposal");
    }

    private static object DisposedHook(Type callback, nint address)
    {
        var hookBase = Type.GetType("Dalamud.Hooking.Hook`1, Dalamud", true)!.MakeGenericType(callback);
        var backend = hookBase.Assembly.GetType("Dalamud.Hooking.Internal.ReloadedHook`1", true)!.MakeGenericType(callback);
        var hook = RuntimeHelpers.GetUninitializedObject(backend);
        // Only base fields used by OriginalDisposeSafe are initialized. A disposed
        // ordinary Original must throw before touching an uninitialized backend.
        Set(hookBase, hook, "address", address);
        Set(hookBase, hook, "<IsDisposed>k__BackingField", true);
        try
        {
            _ = hookBase.GetProperty("Original")!.GetValue(hook);
            throw new Exception("The lifecycle fixture did not enforce Dalamud's disposed Original contract");
        }
        catch (TargetInvocationException error) when (error.InnerException is ObjectDisposedException) { }
        return hook;
    }
    private static void Set(Type type, object target, string name, object value) =>
        (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(type.FullName, name)).SetValue(target, value);
    private static bool Invoke(Type type, object target, string name, object[] args)
    {
        try { return (bool)type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!; }
        catch (TargetInvocationException error) when (error.InnerException is ObjectDisposedException)
        { throw new Exception(name + " accessed a disposed hook Original instead of preserving native playback", error.InnerException); }
    }
}
