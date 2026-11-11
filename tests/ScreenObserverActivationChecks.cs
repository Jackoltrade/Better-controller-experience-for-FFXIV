// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

internal static unsafe class ScreenObserverActivationChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Hidden)!.SetValue(target, value);
    // Reuse the existing managed Hook/DispatchProxy ABI fixtures; no native hook is installed.
    private static object Helper(string name, params object?[] args) => typeof(OwnerLifecycleChecks)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        var type = plugin.GetType("DualSenseLighting.Windows.GameScreenEffects", true)!;
        var diagnostics = ProductionBoundaryChecks.Diagnostics(plugin);
        foreach (var name in diagnostics ? new[] { "UpdateMotion", "SetTracing" } : new[] { "UpdateMotion" })
        {
            var called = Calls(type.GetMethod(name, Hidden)! ).ToArray();
            check(!called.Any(m => m.Name.Contains("Scan", StringComparison.Ordinal) ||
                m is ConstructorInfo && m.DeclaringType?.Name is "SecondaryMotionObserver" or "ScreenEffectSourceObserver"),
                "Compiled " + name + " still scans or constructs an observer during activation");
        }
        foreach (var dev in diagnostics ? new[] { false, true } : new[] { false })
            CheckConstructor(check, plugin, dev);
    }

    private static void CheckConstructor(Action<bool, string> check, Assembly plugin, bool dev)
    {
        var type = plugin.GetType("DualSenseLighting.Windows.GameScreenEffects", true)!;
        var constructor = type.GetConstructors().Single();
        var memory = Marshal.AllocHGlobal(0x3000);
        try
        {
            new Span<byte>((void*)memory, 0x3000).Clear();
            void Put(int at, params byte[] bytes) => Marshal.Copy(bytes, 0, memory + at, bytes.Length);
            var addresses = new Dictionary<string, nint>();
            void Signature(string name, int at) => addresses[(string)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!] = memory + at;
            Signature("PostSignature", 0x100); Signature("CameraSignature", 0x120);
            Marshal.WriteInt32(memory + 0x103, 0x2000 - 0x107); Marshal.WriteInt32(memory + 0x123, 0x2010 - 0x127);
            Signature("CameraUpdateSignature", 0x400); Signature("CameraOffsetsSignature", 0x450);
            Put(0x460, 0x48, 0x63, 0x41, 0x50); Put(0x470, 0x48, 0x8B, 0x4C, 0xC1, 0x58);
            Signature("MixerInputSignature", 0x850); Signature("BlurSignature", 0x900); Signature("ShakeSignature", 0xD00);
            Put(0x900, 0x89, 0x88, 0xA4, 0x46, 0, 0); Put(0x920, 0x89, 0x88, 0xAC, 0x46, 0, 0);
            Put(0x940, 0x89, 0x88, 0xC8, 0x47, 0, 0); Put(0x960, 0xC7, 0x80, 0x9C, 0x46, 0, 0, 2, 0, 0, 0);
            Put(0xD00, 0xF3, 0x0F, 0x11, 0x41, 0x14); Put(0xD20, 0xF3, 0x0F, 0x11, 0x51, 0x34);
            var module = RuntimeHelpers.GetUninitializedObject(typeof(ProcessModule));
            Set(module, "<BaseAddress>k__BackingField", memory); Set(module, "<ModuleMemorySize>k__BackingField", 0x3000);
            var scans = new List<string>();
            var scanner = Helper("Proxy", constructor.GetParameters()[1].ParameterType,
                (Func<MethodInfo, object?[]?, object?>)((method, args) => method.Name switch
                {
                    "get_Module" => module, "get_SearchBase" => memory,
                    "ScanAllText" => Scan((string)args![0]!),
                    _ => throw new NotSupportedException(method.Name),
                }));
            nint[] Scan(string signature) { scans.Add(signature); return addresses.TryGetValue(signature, out var address) ? new[] { address } : Array.Empty<nint>(); }
            int acquired = 0, enabled = 0, disposed = 0;
            var interop = Helper("Interop", constructor.GetParameters()[0].ParameterType,
                (Func<Type, object>)(hookType => { acquired++; return Helper("Hook", hookType,
                    (Action)(() => disposed++), (Action)(() => enabled++), (Action)(() => { })); }));
            var output = RuntimeHelpers.GetUninitializedObject(constructor.GetParameters()[2].ParameterType);
            var settingsType = plugin.GetType("DualSenseLighting.Core.HapticSettings", true)!;
            var settingsCtor = settingsType.GetConstructors().Single(c => c.GetParameters().Length > 1);
            Set(output, "screenSettings", settingsCtor.Invoke(settingsCtor.GetParameters().Select(p => p.DefaultValue).ToArray()));
            var owner = constructor.Invoke(ProductionBoundaryChecks.Diagnostics(plugin)
                ? new[] { interop, scanner, output, (object)dev }
                : new[] { interop, scanner, output });
            var motionType = plugin.GetType("DualSenseLighting.Windows.SecondaryMotionObserver", true)!;
            var sourceType = plugin.GetType("DualSenseLighting.Windows.ScreenEffectSourceObserver");
            string Pattern(Type t, string n) => (string)t.GetField(n, BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
            check(scans.Count(x => x == Pattern(motionType, "ProducerSignature")) == 1 &&
                (sourceType == null || scans.Count(x => x == Pattern(sourceType, "ConstructorSignature")) == (dev ? 1 : 0)),
                "Background constructor did not prepare motion once and quake tracing only for dev installs");
            check(acquired == 1 && enabled == 0 && type.GetField("motionBindingError", Hidden)!.GetValue(owner) != null &&
                (!dev || sourceType == null || type.GetField("sourceTraceError", Hidden)!.GetValue(owner) != null),
                "Optional binding failure poisoned base capture or enabled a hook during preparation");
            var count = scans.Count;
            if (sourceType != null) type.GetMethod("SetTracing")!.Invoke(owner, new object[] { true });
            type.GetMethod("UpdateMotion", Hidden)!.Invoke(owner, new object[] { true, true });
            check(scans.Count == count && (sourceType == null || type.GetField("timing", Hidden)!.GetValue(owner) != null),
                "First tracing/motion activation retried unsupported scanning or lost independent recording");
            // Stop without filesystem work; this fixture checks preparation/activation, not archive IO.
            if (sourceType != null) { Set(owner, "timing", null); Set(owner, "traceSave", Task.CompletedTask); }
            type.GetMethod("Dispose")!.Invoke(owner, null);
            check(disposed == 1, "Prepared base camera hook was not disposed after optional binding failures");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static IEnumerable<MethodBase> Calls(MethodInfo method)
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => (ushort)o.Value);
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        for (var i = 0; i < bytes.Length;)
        {
            ushort key = bytes[i++]; if (key == 0xFE) key = (ushort)(0xFE00 | bytes[i++]);
            var op = opcodes[key];
            if (op.OperandType == OperandType.InlineMethod)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(bytes, i))!;
            i += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, i),
                _ => 4,
            };
        }
    }
}
