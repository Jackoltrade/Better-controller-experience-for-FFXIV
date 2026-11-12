// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Linq;
using System.Reflection;

// Execute the selected compiled flavor, not the linked diagnostic Core copy.
// Managed rumble has no native mixer destination and performs no device writes.
internal static unsafe class CompiledAudioPerformanceChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static object?[] Arguments(MethodBase method, params (string Name, object? Value)[] values) =>
        method.GetParameters().Select(parameter =>
        {
            foreach (var value in values)
                if (string.Equals(value.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)) return value.Value;
            if (parameter.HasDefaultValue) return parameter.DefaultValue;
            return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
        }).ToArray();

    private static object Settings(Type type, params (string Name, object? Value)[] values)
    {
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        return constructor.Invoke(Arguments(constructor, values));
    }

    private static object? Call(object target, string name, params object?[] arguments) =>
        target.GetType().GetMethod(name, All)!.Invoke(target, arguments);

    private static object Property(object target, string name) => target.GetType().GetProperty(name, All)!.GetValue(target)!;

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        var rms = plugin.GetType("DualSenseLighting.Core.SfxLoudness", true)!
            .GetMethod("MeasureRms", All)!.CreateDelegate<PcmAndReportPerformanceChecks.Rms>();
        PcmAndReportPerformanceChecks.CheckRms(check, rms);
        var settingsType = plugin.GetType("DualSenseLighting.Core.HapticSettings", true)!;
        CheckAdmission(check, plugin, settingsType);
        CheckSharedLevels(check, plugin, settingsType);
    }

    private static void CheckAdmission(Action<bool, string> check, Assembly plugin, Type settingsType)
    {
        var policy = plugin.GetType("DualSenseLighting.Core.SfxCapturePolicy", true)!;
        var needsVoice = policy.GetMethod("NeedsVoice", All)!;
        var needsOwnership = policy.GetMethod("NeedsOwnership", All)!;
        var screen = Settings(settingsType, ("ScreenEffects", true));
        var player = Settings(settingsType, ("Enabled", true), ("Strength", .5f));
        var zeroPlayer = Settings(settingsType, ("Enabled", true), ("Strength", 0f));
        foreach (var scenario in new (string Name, object? Settings, bool Lighting, bool PlayerLighting,
            bool Combat, uint Flags, bool Enemies, bool Hits, bool Footsteps, bool Arena, bool Voice, bool Ownership)[]
        {
            ("off", null, false, false, true, 0, false, false, false, false, false, false),
            ("screen only", screen, false, false, true, 0, false, false, false, false, false, false),
            ("zero player strength", zeroPlayer, false, false, true, 0, false, false, false, false, false, false),
            ("player combat", player, false, false, true, 0, false, false, false, false, true, true),
            ("player noncombat", player, false, false, false, 0, false, false, false, false, true, false),
            ("all-combat lighting", null, true, false, true, 0, false, false, false, false, true, false),
            ("player-only lighting", null, true, true, true, 0, false, false, false, false, true, true),
            ("native player lighting", null, true, true, true, 2, false, false, false, false, true, true),
            ("enemy combat", screen, false, false, true, 0, true, false, false, false, true, true),
            ("hit reaction", screen, false, false, true, 0, false, true, false, false, true, true),
            ("footsteps", screen, false, false, false, 0, false, false, true, false, true, false),
            ("arena player exclusion", screen, false, false, false, 0, true, false, false, true, true, true),
            ("unrelated enemy bus", screen, false, false, false, 0, true, false, false, false, true, false),
        })
        {
            var voice = (bool)needsVoice.Invoke(null, Arguments(needsVoice,
                ("lighting", scenario.Lighting), ("settings", scenario.Settings), ("enemies", scenario.Enemies),
                ("hitReactions", scenario.Hits), ("footsteps", scenario.Footsteps)))!;
            var ownership = (bool)needsOwnership.Invoke(null, Arguments(needsOwnership,
                ("combat", scenario.Combat), ("flags", scenario.Flags), ("playerLighting", scenario.PlayerLighting),
                ("settings", scenario.Settings), ("enemies", scenario.Enemies), ("hitReactions", scenario.Hits),
                ("arenaEffects", scenario.Arena)))!;
            check(voice == scenario.Voice && ownership == scenario.Ownership,
                "Compiled audio admission differs for " + scenario.Name);
        }
        foreach (uint flags in new uint[] { 2, 4, 8, 14 })
            check(!(bool)needsOwnership.Invoke(null, Arguments(needsOwnership, ("combat", true), ("flags", flags),
                ("playerLighting", false), ("settings", player), ("enemies", true), ("hitReactions", true)))!,
                "Compiled native haptic flags admitted redundant ownership work: " + flags);
    }

    private static void CheckSharedLevels(Action<bool, string> check, Assembly plugin, Type settingsType)
    {
        var outputType = plugin.GetType("DualSenseLighting.Windows.NativeHapticOutput", true)!;
        var constructor = outputType.GetConstructors().Single();
        var output = constructor.Invoke(new object[] {
            Pointer.Box(null, constructor.GetParameters()[0].ParameterType), (nint)0, (nint)0, (nint)0 });
        var configure = outputType.GetMethod("Configure", All)!;
        var settings = Settings(settingsType, ("Enabled", true), ("Spatial", false), ("DistanceFalloff", false));
        configure.Invoke(output, Arguments(configure, ("requested", settings), ("foreground", true),
            ("attributionSupported", true), ("useRumble", true)));
        var prepare = outputType.GetMethod("Prepare", All)!;
        var frame = prepare.Invoke(output, Arguments(prepare,
            ("voice", Pointer.Box(null, prepare.GetParameters()[0].ParameterType)), ("owned", true),
            ("bus", (byte)2), ("flags", 0u)))!;
        var mix = outputType.GetMethod("Mix", All)!;
        var loudnessType = plugin.GetType("DualSenseLighting.Core.SfxLoudness", true)!;
        var lighting = Activator.CreateInstance(loudnessType)!;
        var rumble = Property(output, "RumbleSignal");
        Call(lighting, "SetActive", true);
        var generation = (uint)Property(lighting, "Generation");
        var expected = MathF.Sqrt(.078125f);
        foreach (var length in new[] { 2048, 4096 })
        {
            var samples = new float[length];
            for (var index = 0; index < length; index++) samples[index] = index % 2 == 0 ? .125f : -.375f;
            fixed (float* pcm = samples)
            {
                object? Mix(object selectedFrame, float* buffer, int count) => mix.Invoke(output,
                    new object[] { selectedFrame, Pointer.Box(buffer, mix.GetParameters()[1].ParameterType), count, 0u });
                var measured = Mix(frame, pcm, length);
                check(measured is float rms && Math.Abs(rms - expected) < .000001f &&
                    samples[0] == .125f && samples[^1] == -.375f && (float)Call(rumble, "TakePeak")! > 0,
                    "Compiled managed rumble changed validated PCM/RMS or lost output: " + length);
                Call(lighting, "ObserveLevel", measured, generation);
                check(Math.Abs((float)Call(lighting, "TakePeak")! - expected) < .000001f,
                    "Compiled lighting did not accept the same validated haptic RMS: " + length);
                foreach (var invalid in new[] { float.NaN, 17f })
                {
                    samples[^1] = invalid;
                    var rejected = Mix(frame, pcm, length);
                    Call(lighting, "ObserveLevel", rejected, generation);
                    check(rejected is float zero && zero == 0 && (float)Call(rumble, "TakePeak")! == 0 &&
                        (float)Call(lighting, "TakePeak")! == 0,
                        "Compiled shared RMS accepted invalid PCM at the end of the block: " + length);
                }
                samples[^1] = -.375f;
                check(Mix(Activator.CreateInstance(frame.GetType())!, pcm, length) == null &&
                    Mix(frame, null, length) == null && Mix(frame, pcm, 0) == null,
                    "Compiled skipped haptic path claimed a validated RMS");
                Array.Clear(samples);
                check(Mix(frame, pcm, length) is float silence && silence == 0,
                    "Compiled silent block invented a nonzero RMS");
            }
        }
        Call(lighting, "SetActive", false); Call(lighting, "SetActive", true);
        Call(lighting, "ObserveLevel", .8f, generation);
        check((float)Call(lighting, "TakePeak")! == 0, "Compiled stale lighting generation republished a shared level");
        generation = (uint)Property(lighting, "Generation");
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, -.1f, 1.1f, -0f })
            Call(lighting, "ObserveLevel", invalid, generation);
        Call(lighting, "ObserveLevel", .25f, generation);
        check((float)Call(lighting, "TakePeak")! == .25f, "Compiled invalid shared level blocked or amplified valid lighting");
        Call(lighting, "SetActive", false);
        Call(lighting, "ObserveLevel", .8f, (uint)Property(lighting, "Generation"));
        check((float)Call(lighting, "TakePeak")! == 0, "Compiled inactive lighting accepted a shared level");
        configure.Invoke(output, Arguments(configure, ("requested", Settings(settingsType)),
            ("foreground", false), ("attributionSupported", false)));
        var buffer = stackalloc float[2048];
        check(mix.Invoke(output, new object[] { frame,
            Pointer.Box(buffer, mix.GetParameters()[1].ParameterType), 2048, 0u }) == null,
            "Compiled retired haptic frame claimed a fresh PCM measurement");
    }
}
