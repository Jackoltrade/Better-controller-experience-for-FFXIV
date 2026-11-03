// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DualSenseLighting.Ui;

public enum UiLanguage { English, Chinese, Japanese }

// English is also the lookup key and fallback. Keep widget IDs in English so
// changing language does not reset selected tabs, open sections or active controls.
internal sealed partial class UiText
{
    private readonly Dictionary<(UiLanguage, string), string> options = new();
    private readonly Dictionary<(UiLanguage, string), string> labels = new();
    public UiLanguage Language { get; set; }

    public string Get(string english) => Translations.TryGetValue(english, out var value)
        ? Language switch { UiLanguage.Chinese => value.Chinese, UiLanguage.Japanese => value.Japanese, _ => english }
        : english;

    public string Label(string english)
    {
        var key = (Language, english);
        if (!labels.TryGetValue(key, out var label)) labels[key] = label = Get(english) + "###" + english;
        return label;
    }
    public string Format(string english, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, Get(english), values);

    public string Options(string english)
    {
        var key = (Language, english);
        if (!options.TryGetValue(key, out var translated))
            options[key] = translated = string.Join('\0', english.Split('\0').Select(Get));
        return translated;
    }

    // Ask Dalamud for only the characters this UI uses, including the language
    // names visible in the switcher before an Asian language is selected.
    private static readonly Lazy<ushort[]> glyphRanges = new(BuildGlyphRanges);
    public static ushort[] GlyphRanges => glyphRanges.Value;
    private static ushort[] BuildGlyphRanges()
    {
        var characters = string.Concat(Translations.Select(p => p.Key + p.Value.Chinese + p.Value.Japanese))
            + "Language / 语言 / 言語 English 简体中文 日本語";
        var points = Enumerable.Range(32, 95).Concat(characters.Where(c => c >= 32 && !char.IsSurrogate(c))
            .Select(c => (int)c)).Distinct().OrderBy(c => c).ToArray();
        var ranges = new List<ushort>();
        for (var i = 0; i < points.Length; i++)
        {
            var first = points[i];
            while (i + 1 < points.Length && points[i + 1] == points[i] + 1) i++;
            ranges.Add((ushort)first);
            ranges.Add((ushort)points[i]);
        }
        ranges.Add(0);
        return ranges.ToArray();
    }
}
