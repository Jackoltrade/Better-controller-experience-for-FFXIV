// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DualSenseLighting.Ui;

internal static class UiTextChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var text = new UiText();
        check(text.Language == UiLanguage.English, "Existing configurations should open in English");
        foreach (var entry in UiText.Translations)
        {
            var original = entry.Key;
            var expected = Placeholders(original);
            var id = text.Label(original).Split("###")[1];
            foreach (var language in Enum.GetValues<UiLanguage>())
            {
                text.Language = language;
                var translated = text.Get(original);
                check(!string.IsNullOrWhiteSpace(translated) && !translated.Contains('\uFFFD'),
                    $"Missing or damaged translation: {language}: {original}");
                check(Placeholders(translated) == expected, $"Format values changed in {language}: {original}");
                var format = CompositeFormat.Parse(translated);
                var args = Enumerable.Range(0, format.MinimumArgumentCount).Select(i => (object)(i + 1.25)).ToArray();
                check(text.Format(original, args) == string.Format(CultureInfo.InvariantCulture, translated, args),
                    $"Translated formatting failed: {language}: {original}");
                check(text.Label(original).Split("###")[1] == id, "Language change reset a widget ID");
                check(text.Label(original).StartsWith(translated + "###", StringComparison.Ordinal),
                    "A localized label did not retain its visible translation");
                foreach (var c in translated)
                    check(Covers(c), $"UI font request is missing {c} from {language}");
            }
            text.Language = UiLanguage.English;
            check(text.Get(original) == original, "English changed after switching languages");
        }

        var options = "Solid\0Rainbow\0Breathing\0Color Shift\0Aurora\0Fire\0Heartbeat\0Comet\0Rainbow Pulse\0SFX Loudness (Prototype)\0SFX Color Blend (Prototype)\0";
        var english = options.Split('\0');
        foreach (var language in new[] { UiLanguage.Chinese, UiLanguage.Japanese, UiLanguage.English, UiLanguage.Chinese })
        {
            text.Language = language;
            var translated = text.Options(options).Split('\0');
            check(translated.Length == english.Length && translated[^1] == "", "Combo item count or terminator changed");
            for (var i = 0; i < english.Length; i++)
                check(translated[i] == text.Get(english[i]), "Language switching changed an effect's saved enum index");
            check(text.Get("Untranslated future text") == "Untranslated future text", "Missing text should fall back to English");
        }
        text.Language = (UiLanguage)999;
        check(text.Get("Lighting") == "Lighting", "Invalid language should fall back to English");
        check(text.Label("Lighting").EndsWith("###Lighting", StringComparison.Ordinal), "Invalid language changed widget identity");
        check(Covers('语') && Covers('简') && Covers('語') && Covers('日'), "Language selector glyphs are missing");
        var ranges = UiText.GlyphRanges;
        check(ranges.Length % 2 == 1 && ranges[^1] == 0, "Font ranges lack their zero terminator");
        for (var i = 0; i < ranges.Length - 1; i += 2)
            check(ranges[i] > 0 && ranges[i] <= ranges[i + 1] && (i == 0 || ranges[i] > ranges[i - 1]),
                "Font glyph ranges are invalid or overlap");
    }

    private static string Placeholders(string value) => string.Join("|", Regex.Matches(value, @"\{\d+(?::[^{}]+)?\}")
        .Select(m => m.Value).OrderBy(m => m, StringComparer.Ordinal));

    private static bool Covers(char value)
    {
        var ranges = UiText.GlyphRanges;
        for (var i = 0; i < ranges.Length - 1; i += 2)
            if (value >= ranges[i] && value <= ranges[i + 1]) return true;
        return char.IsControl(value);
    }
}
