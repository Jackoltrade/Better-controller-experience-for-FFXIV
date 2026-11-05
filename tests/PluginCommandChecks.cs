// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using DualSenseLighting.Core;

internal static class PluginCommandChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var input in new string?[] { null, "", " \t\n" })
            check(PluginCommand.Parse(input).Kind == PluginCommandKind.Open, "Empty command should open settings");
        foreach (var (input, kind) in new[] { ("help", PluginCommandKind.Help), ("INFO", PluginCommandKind.Info), (" trace ", PluginCommandKind.Trace) })
            check(PluginCommand.Parse(input).Kind == kind, "Utility command was routed incorrectly");
        var features = new[]
        {
            ("lighting", ControllerFeature.Lighting),
            ("haptics", ControllerFeature.Haptics),
            ("combathaptics", ControllerFeature.CombatHaptics),
            ("footsteps", ControllerFeature.Footsteps),
            ("spatialhaptics", ControllerFeature.SpatialHaptics),
            ("distancefade", ControllerFeature.DistanceFade),
            ("followimpact", ControllerFeature.FollowImpact),
            ("playersfx", ControllerFeature.PlayerSfx),
        };
        var operations = new[] { "on", "off", "toggle" };
        foreach (var (name, feature) in features)
            for (var operation = 0; operation < operations.Length; operation++)
                foreach (var input in new[] { name + " " + operations[operation],
                    "  " + name.ToUpperInvariant() + "\t" + operations[operation].ToUpperInvariant() + "  " })
                {
                    var parsed = PluginCommand.Parse(input);
                    check(parsed.Kind == PluginCommandKind.Switch && parsed.Feature == feature && (int)parsed.Switch == operation,
                        "A feature command changed meaning with case or whitespace");
                }
        check(PluginCommand.Parse("haptics off").Feature != PluginCommand.Parse("combathaptics off").Feature,
            "Full haptics and combat haptics commands target the same group");
        foreach (var invalid in new[] { "haptics", "haptics false", "haptics off extra", "combathaptics", "combathaptics true", "lighting 1", "0 on", "999 off", "help on",
            "trace now", "info extra", "lighting on extra", "unknown on", "footsteps on;off", "spatialhaptics enable" })
            check(PluginCommand.Parse(invalid).Kind == PluginCommandKind.Invalid, "Malformed command could change a feature");
    }
}
