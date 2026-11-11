// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Actual compiled Render adapter with owned native-layout buffers and a managed
// original-playback sentinel. No live hook, mixer destination or device is used.
internal static unsafe class CompiledRenderChecks
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int originalCalls, originalResult, sampleCount;
    private static bool invalidPcm;
    private static nint originalVoice, originalScratch;

    [UnmanagedCallersOnly]
    private static int Original(nint voice, float* scratch)
    {
        originalCalls++; originalVoice = voice; originalScratch = (nint)scratch;
        for (var index = 0; index < sampleCount; index++) scratch[index] = .1f;
        if (invalidPcm) scratch[sampleCount - 1] = float.NaN;
        return originalResult;
    }

    private static object?[] Arguments(MethodBase method, params (string Name, object? Value)[] values) =>
        method.GetParameters().Select(parameter =>
        {
            foreach (var value in values)
                if (string.Equals(value.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)) return value.Value;
            if (parameter.HasDefaultValue)
                return parameter.ParameterType == typeof(nint)
                    ? (object)(nint)Convert.ToInt64(parameter.DefaultValue) : parameter.DefaultValue;
            return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
        }).ToArray();

    private static object Settings(Type type, params (string Name, object? Value)[] values)
    {
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        return constructor.Invoke(Arguments(constructor, values));
    }
    private static void Set(object target, string name, object? value) =>
        target.GetType().GetField(name, All)!.SetValue(target, value);
    private static void SetPointer(object target, string name, nint value)
    {
        var field = target.GetType().GetField(name, All)!;
        field.SetValue(target, Pointer.Box((void*)value, field.FieldType));
    }
    private static object Property(object target, string name) => target.GetType().GetProperty(name, All)!.GetValue(target)!;
    private static object? Call(object target, string name, params object?[] arguments) =>
        target.GetType().GetMethod(name, All)!.Invoke(target, arguments);
    private static int Offset(Type type, string name) => checked((int)Marshal.OffsetOf(type, name));
    private static void Integer(Type type, nint target, string name, int value) => Marshal.WriteInt32(target + Offset(type, name), value);
    private static void PointerField(Type type, nint target, string name, nint value) => Marshal.WriteIntPtr(target + Offset(type, name), value);
    private static void Float(Type type, nint target, string name, float value) => Integer(type, target, name, BitConverter.SingleToInt32Bits(value));

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        foreach (var format in new uint[] { 0, 1 })
        foreach (var scenario in new (string Name, byte Bus, uint Flags, bool Player, bool Lighting,
            bool Screen, bool Owned, bool Invalid, int Result, float Light, float Rumble, bool NullVoice)[]
        {
            ("screen only", 2, 0, false, false, true, false, false, 0, 0, 0, true),
            ("player feedback and lighting", 2, 0, true, true, false, true, false, 0, .1f, .4f, false),
            ("unrelated bus", 7, 0, true, true, false, true, false, 0, 0, 0, false),
            ("native haptic flag retains player lighting", 2, 2, true, true, false, true, false, 0, .1f, 0, false),
            ("invalid final PCM sample", 2, 0, true, true, false, true, true, 0, 0, 0, false),
            ("nonzero original result retains lighting", 2, 0, true, true, false, true, false, 7, .1f, 0, false),
            ("unowned player-only lighting", 2, 0, true, true, false, false, false, 0, 0, 0, false),
        })
            CheckCase(check, plugin, format, scenario.Name, scenario.Bus, scenario.Flags, scenario.Player,
                scenario.Lighting, scenario.Screen, scenario.Owned, scenario.Invalid, scenario.Result,
                scenario.Light, scenario.Rumble, scenario.NullVoice);
    }

    private static void CheckCase(Action<bool, string> check, Assembly plugin, uint format, string name,
        byte bus, uint flags, bool player, bool lightingActive, bool screen, bool owned, bool invalid,
        int result, float expectedLight, float expectedRumble, bool nullVoice)
    {
        Type T(string name) => plugin.GetType("DualSenseLighting." + name, true)!;
        var allocations = new List<nint>();
        nint Allocate(int bytes)
        {
            var address = (nint)NativeMemory.AllocZeroed((nuint)bytes);
            allocations.Add(address); return address;
        }
        nint Native(Type type) => Allocate(Marshal.SizeOf(type));
        try
        {
            var voiceType = T("Core.GameAudioLayout+SourceVoice"); var voice = Native(voiceType);
            var ownerType = T("Core.GameAudioLayout+VoiceOwner"); var owner = Native(ownerType);
            var trackType = T("Core.GameAudioLayout+Track"); var track = Native(trackType);
            var rootType = T("Core.GameAudioLayout+RootSound"); var root = Native(rootType);
            var soundType = T("Core.GameAudioLayout+SoundData");
            var ownershipType = T("Core.GameAudioLayout+PlayerSoundOwnership");
            var capacity = (int)ownershipType.GetField("Capacity", All)!.GetRawConstantValue()!;
            var pool = Allocate(capacity * Marshal.SizeOf(soundType));
            var table = Allocate(8); var actor = Allocate(32);
            Integer(voiceType, voice, "State", 3); Integer(voiceType, voice, "QueuedBufferCount", 1);
            Integer(voiceType, voice, "SendCount", 1); Integer(voiceType, voice, "OutputFormat", (int)format);
            Float(voiceType, voice, "Pitch", 1); Float(voiceType, voice, "Volume", 1); Float(voiceType, voice, "PreviousVolume", 1);
            PointerField(voiceType, voice, "Callback", owner + Offset(ownerType, "CallbackVTable"));
            PointerField(ownerType, owner, "CallbackVTable", table); PointerField(ownerType, owner, "Track", track);
            PointerField(ownerType, owner, "Source", voice); PointerField(trackType, track, "Voice", owner);
            PointerField(trackType, track, "Owner", root); Integer(rootType, root, "SoundId", 1234);
            Marshal.WriteByte(root + Offset(rootType, "BusId"), bus); Integer(rootType, root, "ExtendedFlags", (int)flags);
            Marshal.WriteByte(pool + Offset(soundType, "IsActive"), 1); Integer(soundType, pool, "SoundId", 1234);
            PointerField(soundType, pool, "ResourceHandle", root);
            var ownershipConstructor = ownershipType.GetConstructors().Single();
            var ownership = ownershipConstructor.Invoke(Arguments(ownershipConstructor,
                ("pool", Pointer.Box((void*)pool, ownershipConstructor.GetParameters()[0].ParameterType))));
            var identityType = T("Core.GameAudioLayout+PlayerIdentity");
            var identity = Activator.CreateInstance(identityType, actor, 1000u, actor + 8)!;
            Set(ownership, "player", identity);
            var acquire = ownershipType.GetMethod("Acquired", All)!;
            acquire.Invoke(ownership, Arguments(acquire,
                ("sound", Pointer.Box((void*)pool, acquire.GetParameters()[0].ParameterType)),
                ("caster", owned ? identity : null)));

            var outputType = T("Windows.NativeHapticOutput");
            var outputConstructor = outputType.GetConstructors().Single();
            var output = outputConstructor.Invoke(new object[] {
                Pointer.Box(null, outputConstructor.GetParameters()[0].ParameterType), (nint)0, (nint)0, (nint)0 });
            var settings = Settings(T("Core.HapticSettings"), ("Enabled", player), ("Strength", .5f),
                ("ScreenEffects", screen), ("Spatial", false), ("DistanceFalloff", false));
            var configure = outputType.GetMethod("Configure", All)!;
            configure.Invoke(output, Arguments(configure, ("requested", settings), ("foreground", true),
                ("attributionSupported", true), ("useRumble", true)));
            var loudness = Activator.CreateInstance(T("Core.SfxLoudness"))!;
            Call(loudness, "SetActive", lightingActive);
            var captureType = T("Windows.SfxAudioCapture");
            var capture = RuntimeHelpers.GetUninitializedObject(captureType);
            Set(capture, "<Loudness>k__BackingField", loudness); Set(capture, "<Haptics>k__BackingField", output);
            // Screen-only operation must not depend on ownership being available.
            Set(capture, "ownership", nullVoice ? null : ownership); Set(capture, "callbackVTable", table);
            var layout = Allocate(12);
            Marshal.WriteInt32(layout, 256); Marshal.WriteInt32(layout + 4, 8); Marshal.WriteInt32(layout + 8, 1);
            SetPointer(capture, "frames", layout); SetPointer(capture, "channels", layout + 4); SetPointer(capture, "spatialAudio", layout + 8);
            var hook = typeof(HookOriginalLifecycleChecks).GetMethod("DisposedHook", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { captureType.GetNestedType("RenderDelegate", BindingFlags.NonPublic)!,
                    (nint)(delegate* unmanaged<nint, float*, int>)&Original });
            Set(capture, "render", hook);
            sampleCount = format == 1 ? 4096 : 2048;
            var pcm = Allocate(sampleCount * sizeof(float));
            originalCalls = 0; originalResult = result; invalidPcm = invalid;
            originalVoice = originalScratch = 0;
            var selectedVoice = nullVoice ? (nint)0 : voice;
            var render = captureType.GetMethod("Render", All)!;
            var actual = render.Invoke(capture, new object[] {
                Pointer.Box((void*)selectedVoice, render.GetParameters()[0].ParameterType),
                Pointer.Box((void*)pcm, render.GetParameters()[1].ParameterType) });
            var label = name + " (format " + format + ")";
            check((int)actual! == result && originalCalls == 1 && originalVoice == selectedVoice && originalScratch == pcm,
                "Compiled Render changed original arguments, result or exactly-once playback: " + label);
            var light = (float)Call(loudness, "TakePeak")!;
            var rumble = (float)Call(Property(output, "RumbleSignal"), "TakePeak")!;
            check(Math.Abs(light - expectedLight) < .000001f && Math.Abs(rumble - expectedRumble) < .000001f,
                $"Compiled Render changed lighting/rumble for {label}: actual {light}/{rumble}, expected {expectedLight}/{expectedRumble}");
            check(!(bool)Property(capture, "Faulted") && !(bool)Property(output, "Faulted") &&
                ((float*)pcm)[0] == .1f && (invalid ? float.IsNaN(((float*)pcm)[sampleCount - 1]) : ((float*)pcm)[sampleCount - 1] == .1f),
                "Compiled Render faulted or modified original PCM: " + label);
        }
        finally { foreach (var address in allocations) NativeMemory.Free((void*)address); }
    }
}
