// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Threading;
using Dalamud.Bindings.ImGui;
using DualSenseLighting.Core;

namespace DualSenseLighting;

public sealed partial class Plugin
{
    private void DrawHaptics()
    {
        ImGui.TextWrapped(T("Detailed DualSense haptics require a USB cable. Bluetooth supports RGB lighting and basic rumble."));
        ImGui.Spacing();
        var enabled = configuration.HapticEnabled;
        if (ImGui.Checkbox(Label("Enable haptics"), ref enabled))
        { configuration.HapticEnabled = enabled; Changed(); }
        Help("The game's own vibration is unaffected.");
        ImGui.BeginDisabled(!enabled);
        try
        {
            if (ImGui.CollapsingHeader(Label("Vibration output"))) DrawHapticOutput();
            Section("Combat");
            DrawCombatHaptics();
            Section("Screen effects");
            DrawScreenEffectHaptics();
            Section("Footstep amplification");
            var footstepsAvailable = HapticOutputSelection.SupportsFootstepAmplification(
                configuration.HapticOutput, session.HasUsb, session.HasBluetooth);
            ImGui.BeginDisabled(!footstepsAvailable);
            try { DrawFootstepHaptics(); }
            finally { ImGui.EndDisabled(); }
            if (!footstepsAvailable)
                ImGui.TextWrapped(T("Connect a DualSense or DualSense Edge by USB and select Automatic or DualSense haptics."));
        }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawHapticOutput()
    {
        var mode = (int)(Enum.IsDefined(configuration.HapticOutput) ? configuration.HapticOutput : HapticOutputMode.Automatic);
        if (ImGui.Combo(Label("Output mode"), ref mode,
            uiText.Options("Automatic\0DualSense haptics\0Regular rumble (XInput)\0DualSense Bluetooth rumble\0")))
        { configuration.HapticOutput = (HapticOutputMode)mode; Changed(); }
        Help("Automatic chooses USB DualSense, then Bluetooth DualSense, then XInput.");
        var output = HapticOutputSelection.Resolve(configuration.HapticOutput, session.HasUsb, session.HasBluetooth);
        if (output == HapticOutputMode.XInput)
        {
            var controller = Math.Clamp(configuration.RumbleController + 1, 0, 4);
            if (ImGui.Combo(Label("Rumble controller"), ref controller,
                uiText.Options("First connected\0Controller 1\0Controller 2\0Controller 3\0Controller 4\0")))
            { configuration.RumbleController = controller - 1; Changed(); }
            Help("XInput uses motor-frequency balance. DirectInput devices need a mapper.");
        }
        if (output == HapticOutputMode.DualSense) return;
        Subsection("Basic rumble");
        DrawBasicRumbleSettings(output == HapticOutputMode.Bluetooth);
    }

    private void DrawBasicRumbleSettings(bool bluetooth)
    {
        var snapshot = Volatile.Read(ref hapticSettings);
        var strength = snapshot.RumbleStrength * 100;
        if (ImGui.SliderFloat(Label("Rumble strength"), ref strength, 0, 100, "%.0f%%"))
        { configuration.RumbleStrength = strength / 100; Changed(); }
        Help("Caps the strength of added combat rumble.");
        var milliseconds = snapshot.RumblePulseMilliseconds;
        if (ImGui.SliderInt(Label("Pulse duration"), ref milliseconds, RumblePulse.MinimumMilliseconds, RumblePulse.MaximumMilliseconds, "%d ms"))
        { configuration.RumblePulseMilliseconds = milliseconds; Changed(); }
        Help("Longer pulses feel heavier. Sustained sounds do not keep the motors buzzing.");
        if (bluetooth)
        {
            var balance = snapshot.BluetoothMotorBalance * 100;
            if (ImGui.SliderFloat(Label("Bluetooth motor balance"), ref balance, -100, 100, "%+.0f%%"))
            { configuration.BluetoothMotorBalance = balance / 100; Changed(); }
            Help("Positive values soften the left motor; negative values soften the right. The motors have different textures.");
        }
        ImGui.BeginDisabled(ActiveRumble.Slot < 0 || strength <= 0);
        try
        {
            if (ImGui.Button(Label("Test rumble strength"))) Interlocked.Exchange(ref rumbleTestRequested, 1);
        }
        finally { ImGui.EndDisabled(); }
        Help("Plays a weak pulse, then a strong one. Other added vibration pauses during the test.");
        ImGui.BeginDisabled(ActiveRumble.Slot < 0 || strength <= 0);
        try
        {
            if (ImGui.Button(Label("Test left/right motors"))) Interlocked.Exchange(ref rumbleTestRequested, 2);
        }
        finally { ImGui.EndDisabled(); }
        Help("Plays left, then right, using your current balance. Commands remain visible in Diagnostics.");
    }

    private void DrawCombatHaptics()
    {
        var combat = configuration.CombatHaptics;
        if (ImGui.Checkbox(Label("Enable combat haptics"), ref combat))
        { configuration.CombatHaptics = combat; Changed(); }
        ImGui.Indent();
        ImGui.BeginDisabled(!combat);
        try
        {
            var player = configuration.CharacterHaptics;
            if (ImGui.Checkbox(Label("Player SFX based"), ref player))
            { configuration.CharacterHaptics = player; Changed(); }
            Help("Follows your skill and weapon sounds.");
            ImGui.BeginDisabled(!player);
            try
            {
                var strength = Volatile.Read(ref hapticSettings).Strength * 100;
                if (ImGui.SliderFloat(Label("Player vibration strength"), ref strength, 0, 100, "%.0f%%"))
                { configuration.HapticStrength = strength / 100; Changed(); }
                if (ImGui.CollapsingHeader(Label("Player SFX settings"))) DrawPlayerImpactSettings();
            }
            finally { ImGui.EndDisabled(); }

            Subsection("Enemy SFX based");
            DrawEnemySfxHaptics();
            ImGui.Spacing();
            if (ImGui.CollapsingHeader(Label("Advanced combat settings"))) DrawCombatSpatialSettings();
        }
        finally { ImGui.EndDisabled(); ImGui.Unindent(); }
    }

    private void DrawEnemySfxHaptics()
    {
        var bosses = configuration.HapticBossEnabled;
        if (ImGui.Checkbox(Label("Boss skills"), ref bosses))
        { configuration.HapticBossEnabled = bosses; Changed(); }
        Help("During duty boss fights, also includes NPC casting, warning cues and basic attacks.");
        var regular = configuration.HapticRegularEnemyEnabled;
        if (ImGui.Checkbox(Label("Regular enemy skills"), ref regular))
        { configuration.HapticRegularEnemyEnabled = regular; Changed(); }
        Help("Excludes basic attacks. Enemies seen before a boss fight stay in this category.");
        var hits = configuration.HapticHitReactions;
        if (ImGui.Checkbox(Label("Player hit reactions"), ref hits))
        { configuration.HapticHitReactions = hits; Changed(); }
        Help("Includes basic attacks. Uses Enemy vibration strength.");
        ImGui.BeginDisabled(!bosses && !regular && !hits);
        try
        {
            var strength = Volatile.Read(ref hapticSettings).EnemyStrength * 100;
            if (ImGui.SliderFloat(Label("Enemy vibration strength"), ref strength, 0, 100, "%.0f%%"))
            { configuration.HapticEnemyStrength = strength / 100; Changed(); }
            Help("Also controls player hit reactions.");
        }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawCombatSpatialSettings()
    {
        Help("Also affects enemy skills and player hit reactions.");
        Subsection("Spatial feedback");
        var spatial = configuration.SpatialHaptics;
        if (ImGui.Checkbox(Label("Spatial haptics"), ref spatial))
        { configuration.SpatialHaptics = spatial; Changed(); }
        Help("Uses your position and the camera's facing to balance the grips.");

        Subsection("Distance");
        var distance = configuration.HapticDistanceFalloff;
        if (ImGui.Checkbox(Label("Distance fade"), ref distance))
        { configuration.HapticDistanceFalloff = distance; Changed(); }
        Help("Sounds without a known position are skipped while enabled.");
        ImGui.BeginDisabled(!distance);
        try { DrawHapticDistanceSettings(); }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawPlayerImpactSettings()
    {
        ImGui.BeginDisabled(!configuration.SpatialHaptics);
        try
        {
            var follow = configuration.HapticFollowImpact;
            if (ImGui.Checkbox(Label("Follow skill impacts"), ref follow))
            { configuration.HapticFollowImpact = follow; Changed(); }
            Help("Steers cast and weapon vibration toward the skill's impact. Requires Spatial haptics.");
            ImGui.BeginDisabled(!follow);
            try
            {
                var bias = Volatile.Read(ref hapticSettings).ImpactBias * 100;
                if (ImGui.SliderFloat(Label("Impact bias"), ref bias, HapticSettings.MinimumImpactBias * 100, 100, "%.0f%%"))
                { configuration.HapticImpactBias = bias / 100; Changed(); }
                Help("Higher values follow the impact more closely. Strength is unchanged.");
            }
            finally { ImGui.EndDisabled(); }
        }
        finally { ImGui.EndDisabled(); }
    }

    private void DrawScreenEffectHaptics()
    {
        var enabled = configuration.HapticScreenEffects;
        if (ImGui.Checkbox(Label("Enable screen effect haptics"), ref enabled))
        { configuration.HapticScreenEffects = enabled; Changed(); }
        Help("Follows camera shake, blur and color-fringe effects. Pauses in PvP.");
        ImGui.BeginDisabled(!enabled);
        try
        {
            var strength = Volatile.Read(ref hapticSettings).ScreenStrength * 100;
            if (ImGui.SliderFloat(Label("Screen effect vibration strength"), ref strength, 0, 100, "%.0f%%"))
            { configuration.HapticScreenStrength = strength / 100; Changed(); }
        }
        finally { ImGui.EndDisabled(); }
        ImGui.TextDisabled(T("Plays alongside other vibration; combined strength is limited."));
        ImGui.TextDisabled(T("Centered feedback; no distance fade."));
    }

    private void DrawFootstepHaptics()
    {
        var footsteps = configuration.FootstepAmplifier;
        if (ImGui.Checkbox(Label("Amplify native footstep vibration"), ref footsteps))
        { configuration.FootstepAmplifier = footsteps; Changed(); }
        Help("Amplifies existing footsteps while unmounted. Requires USB and in-game vibration.");
        ImGui.BeginDisabled(!footsteps);
        try
        {
            var multiplier = Volatile.Read(ref hapticSettings).FootstepMultiplier * 100;
            if (ImGui.SliderFloat(Label("Footstep vibration"), ref multiplier, 100, HapticSettings.MaximumFootstepMultiplier * 100, "%.0f%%"))
            { configuration.FootstepMultiplier = multiplier / 100; Changed(); }
            Help("100% is native strength. Mount and speaker effects are unaffected.");
        }
        finally { ImGui.EndDisabled(); }
        ImGui.TextDisabled(T("Native footstep strength varies by race."));
    }

    private void DrawHapticDistanceSettings()
    {
        var origin = (int)Volatile.Read(ref hapticSettings).DistanceOrigin;
        if (ImGui.Combo(Label("Distance origin"), ref origin, uiText.Options("Character (recommended)\0Camera\0")))
        { configuration.HapticDistanceSource = (HapticDistanceOrigin)origin; Changed(); }
        var automatic = configuration.HapticAutomaticDistance;
        var snapshot = Volatile.Read(ref hapticSettings);
        var fromCamera = snapshot.DistanceOrigin == HapticDistanceOrigin.Camera;
        Help(fromCamera
            ? "Uses the camera's distance to the sound. Zoom affects the fade."
            : "Uses your character's distance to the sound, regardless of zoom.");
        if (ImGui.Checkbox(Label("Use recommended distance fade"), ref automatic))
        { configuration.HapticAutomaticDistance = automatic; Changed(); }
        Help(fromCamera
            ? F("Adds camera distance to the {0:F0}–{1:F0} yalm range.", HapticSettings.RecommendedNearDistance, HapticSettings.RecommendedFarDistance)
            : F("Full strength within {0:F0} yalms; silent at {1:F0}.", HapticSettings.RecommendedNearDistance, HapticSettings.RecommendedFarDistance));
        ImGui.BeginDisabled(automatic);
        try
        {
            snapshot = Volatile.Read(ref hapticSettings);
            var near = snapshot.NearDistance;
            var far = snapshot.FarDistance;
            if (ImGui.SliderFloat(Label("Full strength within (yalms)"), ref near, 0, far - 1, "%.1f"))
            {
                if (fromCamera) configuration.HapticNearDistance = near;
                else configuration.HapticCharacterNearDistance = near;
                Changed();
            }
            if (ImGui.SliderFloat(Label("Fade to zero at (yalms)"), ref far, near + 1, 100, "%.1f"))
            {
                if (fromCamera) configuration.HapticFarDistance = far;
                else configuration.HapticCharacterFarDistance = far;
                Changed();
            }
        }
        finally { ImGui.EndDisabled(); }
    }

}
