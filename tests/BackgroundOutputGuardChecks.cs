// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DualSenseLighting.Core;

// Executes the compiled post-device-validation helper with owned reports. No
// operating-system hook, native device handle or physical output is created.
internal static class BackgroundOutputGuardChecks
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private delegate void Apply(Span<byte> report, bool foreground);

    internal static void Run(Action<bool, string> check, Assembly plugin)
    {
        var type = plugin.GetType("DualSenseLighting.Windows.GameOutputGuard", true)!;
        var guard = RuntimeHelpers.GetUninitializedObject(type);
        void Set(string field, object value) => type.GetField(field, All)!.SetValue(guard, value);
        var apply = type.GetMethod("ApplyLighting", All)!.CreateDelegate<Apply>(guard);
        var requested = LightingSettings.Default with
        {
            Effect = LightingEffect.SfxColorBlend, Brightness = .7f, CycleSeconds = 4,
            Red = 21, Green = 80, Blue = 155, PlayerMode = PlayerIndicatorMode.LimitBreak,
        };
        var time = 12.25;
        var level = .35f;
        var lb = new LimitBreakState(false, 3, 15000, 10000, time);
        var settingsType = type.GetField("settings", All)!.FieldType.GenericTypeArguments[0];
        var lbType = type.GetField("limitBreak", All)!.FieldType.GenericTypeArguments[0];
        object published = ConvertRecord(settingsType, requested);
        object publishedLb = ConvertRecord(lbType, lb);
        var settingsReads = 0; var timeReads = 0; var sfxReads = 0; var lbReads = 0;
        void ResetReads() { settingsReads = timeReads = sfxReads = lbReads = 0; }
        Set("settings", Reader(settingsType, () => { settingsReads++; return published; }));
        Set("seconds", (Func<double>)(() => { timeReads++; return time; }));
        Set("sfxLevel", (Func<float>)(() => { sfxReads++; return level; }));
        Set("limitBreak", Reader(lbType, () => { lbReads++; return publishedLb; }));
        void CheckReads(bool render, string label) => check(settingsReads == 1 && timeReads == (render ? 1 : 0) &&
            sfxReads == (render ? 1 : 0) && lbReads == (render ? 1 : 0),
            "Compiled output guard read unused render inputs: " + label);

        foreach (var length in new[] { 48, 63, 64, 78 })
        {
            var original = NativeReport(length);
            var background = (byte[])original.Clone();
            ResetReads(); apply(background, false); CheckReads(false, "background");
            var expectedBackground = (byte[])original.Clone();
            check(DualSenseReport.Apply(expectedBackground, false, true, default) == ReportAction.BackgroundGuard &&
                background.AsSpan().SequenceEqual(expectedBackground), "Compiled background guard changed its exact report output");
            CheckUnrelated(check, original, background, foreground: false);

            // The very next helper invocation uses current inputs; no timer,
            // background flag cache or deferred refresh is part of this path.
            time += .25; level = .65f;
            lb = lb with { CurrentUnits = 25000, UpdatedAt = time };
            publishedLb = ConvertRecord(lbType, lb);
            var resumed = (byte[])background.Clone();
            var expected = (byte[])background.Clone();
            ResetReads(); apply(resumed, true); CheckReads(true, "foreground resume");
            DualSenseReport.Apply(expected, true, true, LightingEffects.Render(requested, time, level, lb));
            check(resumed.AsSpan().SequenceEqual(expected), "Compiled foreground return delayed or changed current lighting/SFX/LB output");
            CheckUnrelated(check, background, resumed, foreground: true);

            published = ConvertRecord(settingsType, requested with { Enabled = false });
            var disabled = (byte[])original.Clone();
            ResetReads(); apply(disabled, true); CheckReads(false, "foreground disabled");
            check(disabled.AsSpan().SequenceEqual(original), "Disabled custom lighting changed the native report");

            // Disabled custom lighting still needs the same background flag guard.
            disabled = (byte[])original.Clone();
            ResetReads(); apply(disabled, false); CheckReads(false, "background disabled");
            check(disabled.AsSpan().SequenceEqual(expectedBackground), "Disabling lighting removed native background flag filtering");
            published = ConvertRecord(settingsType, requested);
            var enabled = (byte[])original.Clone();
            ResetReads(); apply(enabled, true); CheckReads(true, "foreground enabled again");
            expected = (byte[])original.Clone();
            DualSenseReport.Apply(expected, true, true, LightingEffects.Render(requested, time, level, lb));
            check(enabled.AsSpan().SequenceEqual(expected), "Re-enabling lighting reused stale background or disabled state");
        }

        foreach (var foreground in new[] { false, true })
        {
            var corrupt = NativeReport(78); corrupt[10] ^= 1;
            var before = (byte[])corrupt.Clone();
            ResetReads(); apply(corrupt, foreground); CheckReads(foreground, "corrupt Bluetooth CRC");
            check(corrupt.AsSpan().SequenceEqual(before), "Compiled output helper accepted or rewrote a corrupt native Bluetooth report");
        }
    }

    private static byte[] NativeReport(int length)
    {
        var report = new byte[length]; new Random(20261112 + length).NextBytes(report);
        report[0] = length == 78 ? (byte)0x31 : (byte)2;
        if (length == 78)
        { report[1] = 0x70; report[2] = 0x10; DualSenseReport.SetChecksum(report); }
        return report;
    }

    private static void CheckUnrelated(Action<bool, string> check, byte[] before, byte[] after, bool foreground)
    {
        var bluetooth = before.Length == 78;
        var offset = bluetooth ? 3 : 1;
        for (var index = 0; index < before.Length; index++)
        {
            if (bluetooth && index >= before.Length - 4) continue;
            var common = index - offset;
            if (common == 1 || common == 38 || foreground && common is >= 41 and <= 46) continue;
            check(before[index] == after[index], "Lighting guard changed a native haptic/audio/trigger field at byte " + index);
        }
        if (bluetooth) check(DualSenseReport.HasValidChecksum(after), "Lighting guard produced an invalid Bluetooth checksum");
    }

    private static object ConvertRecord(Type type, object value)
    {
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        return constructor.Invoke(constructor.GetParameters().Select(parameter =>
        {
            var result = value.GetType().GetProperty(parameter.Name!)!.GetValue(value);
            return parameter.ParameterType.IsEnum ? Enum.ToObject(parameter.ParameterType, Convert.ToInt32(result)) : result;
        }).ToArray());
    }

    private static object Reader(Type type, Func<object> read) => typeof(BackgroundOutputGuardChecks)
        .GetMethod(nameof(TypedReader), All)!.MakeGenericMethod(type).Invoke(null, new object[] { read })!;
    private static Func<T> TypedReader<T>(Func<object> read) => () => (T)read();
}
