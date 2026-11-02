// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using Dalamud.Configuration;
using DualSenseLighting.Core;

namespace DualSenseLighting;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public LightingEffect Effect { get; set; }
    public byte Red { get; set; } = 119;
    public byte Green { get; set; } = 255;
    public byte Blue { get; set; }
    public float Brightness { get; set; } = 1;
    public float CycleSeconds { get; set; } = 8;
    public byte PlayerLights { get; set; }
    public PlayerIndicatorAnimation PlayerAnimation { get; set; } = PlayerIndicatorAnimation.Steady;
    public float PlayerCycleSeconds { get; set; } = 2;

    public LightingSettings Snapshot() => new LightingSettings(Enabled, Effect, Red, Green, Blue,
        Brightness, CycleSeconds, PlayerLights, PlayerAnimation, PlayerCycleSeconds).Normalize();
}
