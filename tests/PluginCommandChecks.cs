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
        var names = new[] { "lighting", "combathaptics", "footsteps", "spatialhaptics", "distancefade", "followimpact" };
        var operations = new[] { "on", "off", "toggle" };
        for (var feature = 0; feature < names.Length; feature++)
            for (var operation = 0; operation < operations.Length; operation++)
                foreach (var input in new[] { names[feature] + " " + operations[operation],
                    "  " + names[feature].ToUpperInvariant() + "\t" + operations[operation].ToUpperInvariant() + "  " })
                {
                    var parsed = PluginCommand.Parse(input);
                    check(parsed.Kind == PluginCommandKind.Switch && (int)parsed.Feature == feature && (int)parsed.Switch == operation,
                        "A feature command changed meaning with case or whitespace");
                }
        foreach (var invalid in new[] { "combathaptics", "combathaptics true", "lighting 1", "0 on", "999 off", "help on",
            "trace now", "info extra", "lighting on extra", "unknown on", "footsteps on;off", "spatialhaptics enable" })
            check(PluginCommand.Parse(invalid).Kind == PluginCommandKind.Invalid, "Malformed command could change a feature");
    }
}
