// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DualSenseLighting.Core;

internal static class LimitBreakChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(Marshal.SizeOf<LimitBreakGauge>() == 0x10 && Marshal.OffsetOf<LimitBreakGauge>(nameof(LimitBreakGauge.CurrentUnits)) == 0xA &&
            Marshal.OffsetOf<LimitBreakGauge>(nameof(LimitBreakGauge.IsPvP)) == 0xF, "LB gauge layout changed");
        check(Marshal.SizeOf<LimitBreakActionHeader>() == 0x28 && Marshal.OffsetOf<LimitBreakActionHeader>(nameof(LimitBreakActionHeader.ActionType)) == 0x1F,
            "LB action observation layout changed");

        LimitBreakState Pvp(int units, double time) => new(true, 1, (ushort)units, 10000, time);
        LightingFrame PvpAt(int units, double time) => LimitBreakIndicators.Render(Pvp(units, time), time, 2);
        foreach (var (units, quarters) in new[] { (0, 0), (2499, 0), (2500, 1), (4999, 1), (5000, 2),
            (7499, 2), (7500, 3), (9999, 3), (10000, 4), (11000, 4) })
        {
            check(Pvp(units, 0).ReadyQuarters == quarters, "PvP LB threshold is early or late");
            var eligible = quarters switch { 4 => 27, 3 => 11, 2 => 3, 1 => 1, _ => 0 };
            for (var i = 0; i < 120; i++)
            {
                var frame = PvpAt(units, i / 60.0);
                check((frame.PlayerLights & ~eligible) == 0, "PvP loading used an unearned LED or the excluded center");
                check(frame.PlayerLightsImmediate, "LB indicators enabled bank-wide fading");
            }
        }
        check(PvpAt(2500, 0).PlayerLights == 1 && PvpAt(2500, 1).PlayerLights == 0 && PvpAt(2500, 2).PlayerLights == 1,
            "A single earned PvP indicator did not reuse the normal blink cycle");
        check(PvpAt(5000, 0.3).PlayerLights == 2 && PvpAt(5000, 1.3).PlayerLights == 1,
            "Two-quarter loading did not move one dark dip across otherwise lit indicators");
        check(PvpAt(7500, 0.2).PlayerLights == 10 && PvpAt(7500, 2.6 / 3).PlayerLights == 9 &&
            PvpAt(7500, 4.6 / 3).PlayerLights == 3, "Three-quarter loading did not move its dark dip left to right");
        check(PvpAt(10000, 0).PlayerLights == 27 && PvpAt(10000, 0.999).PlayerLights == 27 &&
            PvpAt(10000, 1).PlayerLights == 0 && PvpAt(10000, 1.999).PlayerLights == 0 && PvpAt(10000, 2).PlayerLights == 27,
            "Full PvP LB did not blink all four together at the configured period");
        for (var i = 0; i < 120; i++)
        {
            var full = PvpAt(10000, i / 60.0);
            check(full.PlayerLights is 0 or 27 && full.PlayerLightsImmediate, "Full PvP blink split the indicators or enabled native fading");
            var loading = PvpAt(7500, i / 60.0);
            check(PlayerIndicatorEffects.Count(loading.PlayerLights) == 2,
                "Loading inserted an all-on pause instead of handing off its single gap");
        }

        // Replaying loading reports, including redundant native game writes, must never enable a shared fade.
        // Otherwise changing one mask bit can make the other earned LEDs flicker as well.
        foreach (var units in new[] { 5000, 7500 })
        {
            var pool = units == 5000 ? 3 : 11;
            var previous = pool & ~1;
            for (var i = 0; i <= 600; i++)
            {
                var frame = PvpAt(units, i / 300.0);
                foreach (var length in new[] { 48, 63, 64 })
                {
                    var report = DualSenseReport.Create(length, frame);
                    var mask = report[44] & 31;
                    check((report[44] & 0x20) != 0 && report[43] == 0,
                        "Loading report restarted shared fading/dimming on LEDs that should stay on");
                    var restored = mask & ~previous;
                    var cleared = previous & ~mask;
                    check((restored == 0 && cleared == 0 || PlayerIndicatorEffects.Count((byte)restored) == 1 &&
                        PlayerIndicatorEffects.Count((byte)cleared) == 1) && (mask & ~pool) == 0,
                        "Loading did not restore one LED and clear the next in the same report");
                    previous = mask;
                }
            }
        }

        foreach (var (units, mask) in new[] { (0, 0), (9999, 0), (10000, 1), (19999, 1), (20000, 5), (29999, 5), (30000, 21) })
        {
            var state = new LimitBreakState(false, 3, (ushort)units, 10000, 0.5);
            check(LimitBreakIndicators.Render(state, 0.5, 2).PlayerLights == mask, "PvE LB completed bars did not blink left/center/right");
            check(LimitBreakIndicators.Render(state with { UpdatedAt = 1.25 }, 1.25, 2).PlayerLights == 0,
                "Ready PvE LB indicators did not switch off together during the blink cycle");
        }
        var twoBars = new LimitBreakState(false, 2, 30000, 10000, 0.5);
        check(twoBars.ReadyBars == 2 && LimitBreakIndicators.Render(twoBars, 0.5, 2).PlayerLights == 5, "PvE LB exceeded duty capacity");
        check(LimitBreakIndicators.Render(Pvp(10000, 0), 0.751, 2).PlayerLights == 0 &&
            LimitBreakIndicators.Render(null, 1, 2).PlayerLights == 0 &&
            LimitBreakIndicators.Render(Pvp(10000, 0), double.NaN, 2).PlayerLights == 0, "Unavailable/stale LB state left lights running");
        check(!(new LimitBreakGauge { BarCount = 3, BarUnits = 10000, IsPvP = 1 }).Valid &&
            !(new LimitBreakGauge { BarCount = 1, IsPvP = 1 }).Valid &&
            !(new LimitBreakGauge { BarCount = 1, BarUnits = 10000, IsPvP = 2 }).Valid, "Unsupported LB gauge was accepted");

        var tracker = new LimitBreakTracker(new Dictionary<uint, bool> { [100] = false, [101] = false, [200] = true, [201] = true });
        var gauge = new LimitBreakGauge { BarCount = 3, CurrentUnits = 20000, BarUnits = 10000 };
        tracker.SetActor(1000, 42, 1, 37, false, true);
        foreach (var (caster, action, type) in new[] { (43u, 100u, (byte)1), (42u, 999u, (byte)1), (42u, 100u, (byte)2), (42u, 201u, (byte)1) })
        {
            tracker.ObserveExecution(caster, action, type, 1, 1);
            check(!tracker.Sample(gauge, false, 0, 0, 1).IsFlashing(1), "A teammate/non-LB/wrong-mode action triggered LB flashing");
        }
        var casting = tracker.Sample(gauge, true, 1, 100, 2);
        check(casting.Casting && LimitBreakIndicators.Render(casting, 2, 2).PlayerLights == 21, "Own LB cast did not start a fast flash");
        check(LimitBreakIndicators.Render(tracker.Sample(gauge, true, 1, 100, 2.125), 2.125, 2).PlayerLights == 0 &&
            LimitBreakIndicators.Render(tracker.Sample(gauge, true, 1, 100, 2.25), 2.25, 2).PlayerLights == 21, "LB cast flash is not four cycles/second");
        tracker.ObserveExecution(42, 100, 1, 2, 2.3);
        check(!tracker.Sample(gauge, false, 0, 0, 2.4).IsFlashing(2.4), "Completed/cancelled LB cast left its override active");
        check(!tracker.Sample(gauge, true, 1, 999, 3).Casting, "An ordinary skill cast triggered LB flashing");
        tracker.ObserveExecution(42, 101, 1, 3, 4);
        check(tracker.Sample(gauge, false, 0, 0, 4.2).IsFlashing(4.2), "Confirmed instant LB did not flash");
        tracker.ObserveExecution(42, 101, 1, 3, 4.7);
        check(!tracker.Sample(gauge, false, 0, 0, 4.81).IsFlashing(4.81), "Duplicate action effects extended instant LB flashing");
        tracker.ObserveExecution(42, 101, 1, 4, 5);
        tracker.SetActor(1000, 42, 1, 37, false, false);
        tracker.SetActor(1000, 42, 1, 37, false, true);
        check(!tracker.Sample(gauge, false, 0, 0, 5.1).IsFlashing(5.1), "Background LB execution survived focus release");
        foreach (var (address, territory, job) in new[] { ((nint)1000, 2u, 37u), ((nint)1000, 2u, 19u), ((nint)2000, 2u, 19u) })
        {
            tracker.ObserveExecution(42, 101, 1, 5, 6);
            tracker.SetActor(address, 42, territory, job, false, true);
            check(!tracker.Sample(gauge, false, 0, 0, 6.1).IsFlashing(6.1), "LB execution leaked across territory/job/actor changes");
        }
        tracker.SetActor(2000, 42, 3, 37, true, true);
        gauge.IsPvP = 1; gauge.BarCount = 1; gauge.CurrentUnits = 0;
        tracker.ObserveExecution(42, 201, 14, uint.MaxValue, 7);
        var instant = tracker.Sample(gauge, false, 0, 0, 7);
        check(instant.IsFlashing(7) && LimitBreakIndicators.Render(instant, 7, 2).PlayerLights == 27, "Instant PvP LB did not flash all four LEDs after spending its gauge");
        tracker.ObserveExecution(42, 201, 1, 0, 8);
        check(tracker.Sample(gauge, false, 0, 0, 8.1).IsFlashing(8.1), "Action sequence wrap lost a PvP LB use");

        var settings = LightingSettings.Default with { PlayerMode = PlayerIndicatorMode.LimitBreak, PlayerLights = 31 };
        foreach (var effect in Enum.GetValues<LightingEffect>())
        {
            var configured = settings with { Effect = effect };
            var expectedColor = LightingEffects.Render(configured with { PlayerMode = PlayerIndicatorMode.Animation }, 0.5, 0.7f);
            var frame = LightingEffects.Render(configured, 0.5, 0.7f, Pvp(10000, 0.5));
            check(frame.Red == expectedColor.Red && frame.Green == expectedColor.Green && frame.Blue == expectedColor.Blue,
                "LB indicators changed RGB/SFX output");
            check(frame == ControllerPreviewGeometry.Frame(configured, true, 0.5, 0.7f, Pvp(10000, 0.5)), "LB hardware renderer and preview disagree");
            check(ControllerPreviewGeometry.Frame(configured, false, 0.5, 0.7f, Pvp(10000, 0.5)) == default, "Background LB preview stayed lit");
            foreach (var length in new[] { 48, 63, 64 })
            {
                var report = DualSenseReport.Create(length, frame);
                check(report[43] == 0 && (report[44] & 31) == frame.PlayerLights &&
                    report[45] == frame.Red && report[46] == frame.Green && report[47] == frame.Blue, "LB HID report diverged from its preview frame");
                check((report[44] & 0x20) != 0, "LB blink report re-enabled the shared native fade");
                for (var i = 3; i < 39; i++) check(report[i] == 0, "LB output enabled haptics/speaker/trigger controls");
            }
        }
        var unavailable = LightingEffects.Render(settings, 0.5);
        check(unavailable.PlayerLights == 0 && unavailable.Green == 255, "Missing LB state fell back to the manual LEDs or disabled RGB");
        check((settings with { PlayerMode = (PlayerIndicatorMode)99 }).Normalize().PlayerMode == PlayerIndicatorMode.Animation,
            "Unknown saved indicator mode did not preserve manual animation behavior");
    }
}
