// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

public enum PluginCommandKind { Invalid, Open, Help, Info, Trace, Switch }
public enum ControllerFeature { Lighting, CombatHaptics, Footsteps, SpatialHaptics, DistanceFade, FollowImpact, PlayerSfx, Haptics }
public enum FeatureSwitch { On, Off, Toggle }

public readonly record struct PluginCommand(PluginCommandKind Kind, ControllerFeature Feature = default, FeatureSwitch Switch = default)
{
    public static PluginCommand Parse(string? arguments)
    {
        var words = (arguments ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return new(PluginCommandKind.Open);
        if (words.Length == 1)
            return new(words[0].ToLowerInvariant() switch
            {
                "help" => PluginCommandKind.Help,
                "info" => PluginCommandKind.Info,
                "trace" => PluginCommandKind.Trace,
                _ => PluginCommandKind.Invalid,
            });
        if (words.Length != 2) return new(PluginCommandKind.Invalid);
        ControllerFeature? feature = words[0].ToLowerInvariant() switch
        {
            "lighting" => ControllerFeature.Lighting,
            "haptics" => ControllerFeature.Haptics,
            "combathaptics" => ControllerFeature.CombatHaptics,
            "playersfx" => ControllerFeature.PlayerSfx,
            "footsteps" => ControllerFeature.Footsteps,
            "spatialhaptics" => ControllerFeature.SpatialHaptics,
            "distancefade" => ControllerFeature.DistanceFade,
            "followimpact" => ControllerFeature.FollowImpact,
            _ => null,
        };
        FeatureSwitch? operation = words[1].ToLowerInvariant() switch
        {
            "on" => FeatureSwitch.On,
            "off" => FeatureSwitch.Off,
            "toggle" => FeatureSwitch.Toggle,
            _ => null,
        };
        return feature.HasValue && operation.HasValue
            ? new(PluginCommandKind.Switch, feature.Value, operation.Value) : new(PluginCommandKind.Invalid);
    }
}
