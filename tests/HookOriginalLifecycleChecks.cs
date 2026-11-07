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
    private static nint sourceSelf, sourceOwner, sourceParent, sourceResource, sourceWrapper;
    private static uint sourceArg1Bits, sourceArg2Bits;
    [UnmanagedCallersOnly]
    private static int OriginalWrite(nint file, nint buffer, uint length, nint written, nint overlapped)
    { calls++; lastFile = file; lastLength = length; return 1; }
    [UnmanagedCallersOnly]
    private static byte OriginalOutput(nint file, nint buffer, uint length)
    { calls++; lastFile = file; lastLength = length; return 1; }

    [UnmanagedCallersOnly]
    private static nint OriginalSourceConstructor(nint self, nint owner, nint parent, nint resource,
        nint wrapper, float arg6, float arg7)
    {
        calls++; sourceSelf = self; sourceOwner = owner; sourceParent = parent;
        sourceResource = resource; sourceWrapper = wrapper;
        sourceArg1Bits = BitConverter.SingleToUInt32Bits(arg6); sourceArg2Bits = BitConverter.SingleToUInt32Bits(arg7);
        return 0x1234567;
    }

    [UnmanagedCallersOnly]
    private static void OriginalSourceSample(nint self, float arg1, float arg2)
    {
        calls++; sourceSelf = self;
        sourceArg1Bits = BitConverter.SingleToUInt32Bits(arg1); sourceArg2Bits = BitConverter.SingleToUInt32Bits(arg2);
    }

    private static Type rotationType = null!;
    private static nint rotationContext, rotationVector;
    private static int rotationIndex, setterKind;
    private static bool producerScopeCleared;
    [UnmanagedCallersOnly]
    private static void OriginalRotationProducer(nint source, nint context)
    {
        calls++; sourceSelf = source; rotationContext = context;
        producerScopeCleared = rotationType.GetField("scope", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) == null;
    }
    [UnmanagedCallersOnly]
    private static void OriginalRotationReset(nint source)
    { calls++; sourceSelf = source; }
    [UnmanagedCallersOnly]
    private static void OriginalRotationSetter(nint manager, int index, nint vector)
    { setterKind = 1; calls++; sourceSelf = manager; rotationIndex = index; rotationVector = vector; }

    [UnmanagedCallersOnly]
    private static void OriginalPositionSetter(nint manager, int index, nint vector)
    { setterKind = 2; calls++; sourceSelf = manager; rotationIndex = index; rotationVector = vector; }

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
        CheckSourceForwarding(check, plugin);
        CheckRotationForwarding(check, plugin);
    }

    private static void CheckSourceForwarding(Action<bool, string> check, Assembly plugin)
    {
        var type = plugin.GetType("DualSenseLighting.Windows.ScreenEffectSourceObserver", true)!;
        var observer = RuntimeHelpers.GetUninitializedObject(type);
        Set(type, observer, "constructor", DisposedHook(type.GetNestedType("ConstructorDelegate", BindingFlags.NonPublic)!,
            (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, float, float, nint>)&OriginalSourceConstructor));
        Set(type, observer, "sampler", DisposedHook(type.GetNestedType("SampleDelegate", BindingFlags.NonPublic)!,
            (nint)(delegate* unmanaged<nint, float, float, void>)&OriginalSourceSample));
        // Disabled capture and a callback already entered when disposal completed
        // must both preserve native playback without reading fake source memory.
        foreach (var disposed in new[] { 0, 1 })
        {
            Set(type, observer, "disposed", disposed);
            calls = 0;
            var result = type.GetMethod("Construct", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(observer,
                new object[] { (nint)101, (nint)202, (nint)303, (nint)404, (nint)505,
                    BitConverter.UInt32BitsToSingle(0x7fc01234), BitConverter.UInt32BitsToSingle(0x80000000) });
            check((nint)result! == 0x1234567 && calls == 1 && sourceSelf == 101 && sourceOwner == 202 &&
                sourceParent == 303 && sourceResource == 404 && sourceWrapper == 505 &&
                sourceArg1Bits == 0x7fc01234 && sourceArg2Bits == 0x80000000,
                "Disabled/retired source construction changed original arguments, float bits, return value or call count");
            type.GetMethod("Sample", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(observer,
                new object[] { (nint)606, float.PositiveInfinity, BitConverter.UInt32BitsToSingle(0x7fc05678) });
            check(calls == 2 && sourceSelf == 606 && sourceArg1Bits == 0x7f800000 && sourceArg2Bits == 0x7fc05678,
                "Disabled/retired source sampling changed original arguments, float bits or call count");
        }
    }

    private static void CheckRotationForwarding(Action<bool, string> check, Assembly plugin)
    {
        rotationType = plugin.GetType("DualSenseLighting.Windows.SecondaryMotionObserver", true)!;
        var owner = RuntimeHelpers.GetUninitializedObject(rotationType);
        Set(rotationType, owner, "producer", DisposedHook(rotationType.GetNestedType("ProducerDelegate", BindingFlags.NonPublic)!,
            (nint)(delegate* unmanaged<nint, nint, void>)&OriginalRotationProducer));
        Set(rotationType, owner, "rotationSetter", DisposedHook(rotationType.GetNestedType("SetterDelegate", BindingFlags.NonPublic)!,
            (nint)(delegate* unmanaged<nint, int, nint, void>)&OriginalRotationSetter));
        Set(rotationType, owner, "reset", DisposedHook(rotationType.GetNestedType("ResetDelegate", BindingFlags.NonPublic)!,
            (nint)(delegate* unmanaged<nint, void>)&OriginalRotationReset));
        Set(rotationType, owner, "positionSetter", DisposedHook(rotationType.GetNestedType("SetterDelegate", BindingFlags.NonPublic)!,
            (nint)(delegate* unmanaged<nint, int, nint, void>)&OriginalPositionSetter));
        var sessionType = plugin.GetType("DualSenseLighting.Windows.SecondaryMotionSession", true)!;
        var session = RuntimeHelpers.GetUninitializedObject(sessionType);
        var scopeType = rotationType.GetNestedType("Scope", BindingFlags.NonPublic)!;
        var outer = Activator.CreateInstance(scopeType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new object[] { owner, session, 11L, 22L, 33L }, null)!;
        var scopeField = rotationType.GetField("scope", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            foreach (var disposed in new[] { 0, 1 })
            {
                Set(rotationType, owner, "disposed", disposed);
                scopeField.SetValue(null, outer); calls = 0; producerScopeCleared = false;
                rotationType.GetMethod("Produce", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner,
                    new object[] { (nint)707, (nint)808 });
                check(calls == 1 && sourceSelf == 707 && rotationContext == 808 && producerScopeCleared &&
                    Equals(scopeField.GetValue(null), outer),
                    "Disabled/retired rotation producer changed native arguments/call count, inherited an outer scope, or failed to restore it");
                rotationType.GetMethod("SetRotation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner,
                    new object[] { (nint)909, -17, (nint)1010 });
                check(calls == 2 && setterKind == 1 && sourceSelf == 909 && rotationIndex == -17 && rotationVector == 1010 &&
                    Equals(scopeField.GetValue(null), outer),
                    "Disabled/retired rotation setter changed signed index, pointers, original count or outer scope");
                rotationType.GetMethod("SetPosition", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner,
                    new object[] { (nint)1212, -29, (nint)1313 });
                check(calls == 3 && setterKind == 2 && sourceSelf == 1212 && rotationIndex == -29 && rotationVector == 1313 &&
                    Equals(scopeField.GetValue(null), outer),
                    "Disabled/retired position setter changed signed index, pointers, original count or outer scope");
                rotationType.GetMethod("ResetSource", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner,
                    new object[] { (nint)1111 });
                check(calls == 4 && sourceSelf == 1111 && Equals(scopeField.GetValue(null), outer),
                    "Disabled/retired rotation reset changed its original argument, call count or active scope");
            }
            Set(rotationType, owner, "disposed", 0);
            Set(rotationType, owner, "enabled", 1);
            var currentType = typeof(Func<>).MakeGenericType(sessionType);
            var current = System.Linq.Expressions.Expression.Lambda(currentType,
                System.Linq.Expressions.Expression.Constant(session, sessionType)).Compile();
            Set(rotationType, owner, "current", current);
            scopeField.SetValue(null, outer); calls = 0; producerScopeCleared = false;
            rotationType.GetMethod("Produce", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner,
                new object[] { (nint)0, (nint)818 });
            check(calls == 1 && sourceSelf == 0 && rotationContext == 818 && producerScopeCleared &&
                Equals(scopeField.GetValue(null), outer),
                "An invalid active rotation producer inherited stale scope or failed native forwarding/restoration");
        }
        finally { scopeField.SetValue(null, null); }
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
