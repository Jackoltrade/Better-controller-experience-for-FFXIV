// SPDX-License-Identifier: GPL-3.0-only
// Preference parsing reuses the earlier local Steam Lighting Guard prototype.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DualSenseLighting.Core;

public sealed class SteamLightingProfiles(string steamRoot, Func<string?> activeAccount)
{
    public LightingFrame Read(string? serial, int controllerCount)
    {
        var id = NormalizeSerial(serial);
        if (id == null) throw new InvalidDataException("Could not identify this controller's Steam lighting profile.");
        var root = Path.Combine(steamRoot, "steamapps", "common", "Steam Controller Configs");
        var name = "preferences_DS" + id + ".vdf";
        var account = activeAccount();
        if (account != null && Regex.IsMatch(account, "^[0-9]+$"))
        {
            var preferred = Path.Combine(root, account, "config", name);
            if (File.Exists(preferred)) return Parse(File.ReadAllText(preferred), controllerCount);
        }

        // Only accept an unambiguous controller profile when Steam's active account is unknown.
        var candidates = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Select(d => Path.Combine(d, "config", name)).Where(File.Exists).ToArray()
            : [];
        if (candidates.Length != 1)
            throw new InvalidDataException(candidates.Length == 0
                ? "No saved Steam LED settings found for this controller. Save them in Steam's Controller settings."
                : "Multiple Steam accounts have LED settings for this controller. Sign in to the intended Steam account.");
        return Parse(File.ReadAllText(candidates[0]), controllerCount);
    }

    public static string? NormalizeSerial(string? serial)
    {
        var value = serial?.Replace(":", "").Replace("-", "").Trim().ToLowerInvariant();
        return value != null && value != "000000000000" && Regex.IsMatch(value, "^[0-9a-f]{12}$") ? value : null;
    }

    public static LightingFrame Parse(string text, int controllerCount)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(text, "\"([^\"\\r\\n]+)\"\\s+\"([^\"\\r\\n]*)\""))
            values[match.Groups[1].Value] = match.Groups[2].Value;

        double Number(string key, double max)
        {
            if (!values.TryGetValue(key, out var raw) ||
                !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < 0 || value > max)
                throw new InvalidDataException($"Missing or invalid Steam lighting setting: {key}");
            return value;
        }

        var brightness = Number("guide_brightness", 1);
        var saturation = Number("color_saturation", 1);
        byte Channel(string key) => (byte)Math.Round((255 * (1 - saturation) + Number(key, 255) * saturation) * brightness);
        var mode = Number("player_slot_led", 2);
        if (mode != Math.Truncate(mode)) throw new InvalidDataException("Invalid Steam player LED mode.");
        var player = mode == 2 || (mode == 1 && controllerCount > 1) ? (byte)4 : (byte)0;
        return new(Channel("color_red"), Channel("color_green"), Channel("color_blue"), player);
    }
}
