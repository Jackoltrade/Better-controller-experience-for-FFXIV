// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using DualSenseLighting;
using DualSenseLighting.Core;
using DualSenseLighting.Ui;

// The configuration contract is the only Dalamud dependency of Configuration.cs.
// Keep these tests independent of a game installation while compiling the actual class.
namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}

internal static class HapticConfigurationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        for (var mask = 0; mask < 64; mask++)
        {
            var config = new Configuration
            {
                CharacterHaptics = (mask & 1) != 0,
                HapticBossEnabled = (mask & 2) != 0,
                HapticRegularEnemyEnabled = (mask & 4) != 0,
                FootstepAmplifier = (mask & 8) != 0,
                HapticHitReactions = (mask & 16) != 0,
                HapticScreenEffects = (mask & 32) != 0, HapticScreenStrength = 0.33f,
                HapticStrength = 0.63f, HapticEnemyStrength = 0.42f, FootstepMultiplier = 3,
                HapticImpactBias = 0.37f, HapticAutomaticDistance = false,
                HapticCharacterNearDistance = 4, HapticCharacterFarDistance = 21,
                Language = UiLanguage.Japanese, Red = 57, HasSeenGeneralInfo = true,
            };
            var saved = JsonSerializer.Serialize(config);
            var lighting = config.Snapshot();
            var selected = config.HapticsSnapshot();
            check(selected.Enabled == ((mask & 1) != 0) && selected.BossEnabled == ((mask & 2) != 0) &&
                selected.RegularEnemyEnabled == ((mask & 4) != 0) && selected.FootstepAmplifier == ((mask & 8) != 0) &&
                selected.HitReactions == ((mask & 16) != 0) && selected.ScreenEffects == ((mask & 32) != 0) && selected.ScreenStrength == 0.33f,
                "Master-on did not preserve the selected haptic categories");

            // Exercise every category selection with both independent group switches.
            // Combat pauses its four sources, while footsteps and screen effects need only the full master.
            for (var switches = 0; switches < 4; switches++)
            {
                config.HapticEnabled = (switches & 1) != 0;
                config.CombatHaptics = (switches & 2) != 0;
                var effective = config.HapticsSnapshot();
                var master = config.HapticEnabled; var combat = master && config.CombatHaptics;
                check(effective.Enabled == (combat && config.CharacterHaptics) &&
                    effective.BossEnabled == (combat && config.HapticBossEnabled) &&
                    effective.RegularEnemyEnabled == (combat && config.HapticRegularEnemyEnabled) &&
                    effective.HitReactions == (combat && config.HapticHitReactions) &&
                    effective.FootstepAmplifier == (master && config.FootstepAmplifier) &&
                    effective.ScreenEffects == (master && config.HapticScreenEffects),
                    "Master/combat switches changed category choices or gated a non-combat source with combat");
                check(effective.CombatEnabled == (combat && (mask & 23) != 0) &&
                    effective.NeedsAudio == (master && ((mask & 40) != 0 || config.CombatHaptics && (mask & 23) != 0)),
                    "The audio requirement did not follow the effective combat, footstep and screen categories");
                check(effective.AdditionalGain(true, 2, 0, false) == (combat && config.CharacterHaptics ? 0.63f : 0) &&
                    effective.AdditionalGain(false, 10, 0, false, EnemySoundKind.Boss) == (combat && config.HapticBossEnabled ? 0.42f : 0) &&
                    effective.AdditionalGain(false, 2, 0, false, EnemySoundKind.Regular) == (combat && config.HapticRegularEnemyEnabled ? 0.42f : 0) &&
                    effective.AdditionalGain(true, 2, 0, false, hitReaction: true) == (combat && config.HapticHitReactions ? 0.42f : 0) &&
                    effective.AdditionalGain(false, 5, 0, false, EnemySoundKind.Boss, arenaEffect: true) == (combat && config.HapticBossEnabled ? 0.42f : 0) &&
                    effective.NativeFootstepExtraGain(6, true, 1) == (master && config.FootstepAmplifier ? 2 : 0),
                    "A disabled group leaked feedback or combat-off suppressed the independent footstep send");
            }
            config.HapticEnabled = config.CombatHaptics = true;
            config.SetFeature(ControllerFeature.Haptics, FeatureSwitch.Off);
            var paused = config.HapticsSnapshot();
            check(!paused.NeedsAudio && !paused.Enabled && !paused.EnemiesEnabled && !paused.FootstepAmplifier && !paused.HitReactions && !paused.ScreenEffects &&
                paused.AdditionalGain(true, 2, 0, false) == 0 &&
                paused.AdditionalGain(false, 2, 0, false, EnemySoundKind.Boss) == 0 &&
                paused.AdditionalGain(false, 2, 0, false, EnemySoundKind.Regular) == 0 &&
                paused.NativeFootstepExtraGain(6, true, 1) == 0,
                "Master-off leaked player, boss, regular-enemy or amplified-footstep feedback");
            check(config.Snapshot() == lighting, "The haptic master switch changed lighting");
            var reloaded = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;
            check(!reloaded.HapticEnabled && !reloaded.HapticsSnapshot().NeedsAudio,
                "Reloading silently enabled paused haptics");
            reloaded.SetFeature(ControllerFeature.Haptics, FeatureSwitch.On);
            check(reloaded.HapticsSnapshot() == selected && JsonSerializer.Serialize(reloaded) == saved,
                "Master toggle or reload cleared category choices, levels, distance settings or unrelated preferences");

            config.HapticEnabled = true;
            config.SetFeature(ControllerFeature.CombatHaptics, FeatureSwitch.Off);
            var combatPaused = config.HapticsSnapshot();
            check(!combatPaused.CombatEnabled && combatPaused.FootstepAmplifier == selected.FootstepAmplifier &&
                combatPaused.ScreenEffects == selected.ScreenEffects && config.Snapshot() == lighting,
                "Combat-off disabled footsteps, screen effects or lighting");
            reloaded = JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;
            check(reloaded.HapticEnabled && !reloaded.CombatHaptics && reloaded.HapticsSnapshot() == combatPaused,
                "Reloading lost the independent combat pause");
            reloaded.SetFeature(ControllerFeature.CombatHaptics, FeatureSwitch.On);
            check(reloaded.HapticsSnapshot() == selected && JsonSerializer.Serialize(reloaded) == saved,
                "Combat pause/reload erased a category or changed a saved setting");

            // Existing configurations omitted the new combat group, and older ones also omitted the master.
            var oldJson = saved.Replace("\"CombatHaptics\":true,", "");
            check(!oldJson.Contains("\"CombatHaptics\"", StringComparison.Ordinal),
                "The legacy migration fixture still serialized the new combat switch");
            var old = JsonSerializer.Deserialize<Configuration>(oldJson)!;
            check(old.HapticEnabled && old.CombatHaptics && old.HapticsSnapshot() == selected,
                "Adding the combat switch changed an existing configuration's selected behavior");
            old = JsonSerializer.Deserialize<Configuration>(oldJson.Replace("\"HapticEnabled\":true,", ""))!;
            check(old.HapticEnabled && old.CombatHaptics && old.HapticsSnapshot() == selected,
                "A configuration predating both group switches lost its existing choices");
            old = JsonSerializer.Deserialize<Configuration>(oldJson.Replace("\"HapticEnabled\":true", "\"HapticEnabled\":false"))!;
            check(!old.HapticEnabled && old.CombatHaptics && !old.HapticsSnapshot().NeedsAudio,
                "The new combat default bypassed an existing disabled master");
        }

        var commands = new Configuration { HapticEnabled = false, CombatHaptics = false, CharacterHaptics = false };
        commands.SetFeature(ControllerFeature.PlayerSfx, FeatureSwitch.On);
        commands.SetFeature(ControllerFeature.Footsteps, FeatureSwitch.On);
        check(!commands.HapticEnabled && commands.CharacterHaptics && commands.FootstepAmplifier && !commands.HapticsSnapshot().NeedsAudio,
            "A category command bypassed or silently enabled the master switch");
        commands.SetFeature(ControllerFeature.CombatHaptics, FeatureSwitch.On);
        check(!commands.HapticEnabled && commands.CombatHaptics && !commands.HapticsSnapshot().NeedsAudio,
            "The combat command silently enabled the full haptic master");
        commands.SetFeature(ControllerFeature.Haptics, FeatureSwitch.On);
        check(commands.HapticsSnapshot() is { Enabled: true, FootstepAmplifier: true },
            "The master command failed to resume saved category commands");
        commands.SetFeature(ControllerFeature.CombatHaptics, FeatureSwitch.Off);
        check(commands.HapticEnabled && commands.CharacterHaptics &&
            commands.HapticsSnapshot() is { Enabled: false, FootstepAmplifier: true },
            "The combat command changed the master, saved player choice or independent footsteps");

        var properties = typeof(Configuration).GetProperties().Where(p => p.CanRead && p.CanWrite).ToArray();
        foreach (var feature in Enum.GetValues<ControllerFeature>())
        {
            var config = new Configuration();
            var before = properties.ToDictionary(p => p.Name, p => p.GetValue(config));
            config.SetFeature(feature, FeatureSwitch.Toggle);
            var changed = properties.Where(p => !Equals(before[p.Name], p.GetValue(config))).Select(p => p.Name).ToArray();
            var expectedProperty = feature switch
            {
                ControllerFeature.Lighting => nameof(Configuration.Enabled),
                ControllerFeature.Haptics => nameof(Configuration.HapticEnabled),
                ControllerFeature.CombatHaptics => nameof(Configuration.CombatHaptics),
                ControllerFeature.PlayerSfx => nameof(Configuration.CharacterHaptics),
                ControllerFeature.Footsteps => nameof(Configuration.FootstepAmplifier),
                ControllerFeature.SpatialHaptics => nameof(Configuration.SpatialHaptics),
                ControllerFeature.DistanceFade => nameof(Configuration.HapticDistanceFalloff),
                ControllerFeature.FollowImpact => nameof(Configuration.HapticFollowImpact),
                _ => throw new ArgumentOutOfRangeException(nameof(feature)),
            };
            check(changed.Length == 1 && changed[0] == expectedProperty &&
                config.IsFeatureEnabled(feature) == !(bool)before[expectedProperty]!,
                "A feature command changed the wrong setting or reported a different feature state");
            check(!config.SetFeature(feature, config.IsFeatureEnabled(feature) ? FeatureSwitch.On : FeatureSwitch.Off),
                "Reapplying the current feature state reported a change");
        }
        commands.HapticEnabled = false;
        commands.CombatHaptics = false;
        commands.Language = UiLanguage.Chinese;
        commands.Red = 17;
        commands.ResetHapticsToDefaults();
        check(commands.HapticEnabled && commands.CombatHaptics && commands.HapticsSnapshot() == new Configuration().HapticsSnapshot() &&
            commands.Language == UiLanguage.Chinese && commands.Red == 17,
            "Reset did not restore the master/category defaults or changed another tab/language");
        commands.HapticEnabled = false;
        commands.CombatHaptics = false;
        commands.ResetLightingToDefaults();
        check(!commands.HapticEnabled && !commands.CombatHaptics, "Resetting lighting changed a haptic group switch");
    }
}
