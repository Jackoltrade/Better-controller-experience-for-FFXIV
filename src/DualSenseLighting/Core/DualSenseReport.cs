// SPDX-License-Identifier: GPL-3.0-only
// USB lighting offsets/flags adapted from DS4Windows DualSenseDevice.
// Original DS4Windows portions Copyright (C) 2023 Travis Nickles.
// See THIRD-PARTY-NOTICES.txt for the pinned upstream source.
#nullable enable
using System;

namespace DualSenseLighting.Core;

public enum ReportAction { Unchanged, CustomLighting, BackgroundGuard }

public static class DualSenseReport
{
    public static bool IsSupportedLength(int length) => length is 48 or 63 or 64;

    public static ReportAction Apply(Span<byte> report, bool foreground, bool enabled, LightingFrame frame)
    {
        if (!IsSupportedLength(report.Length) || report[0] != 0x02) return ReportAction.Unchanged;
        if (!foreground)
        {
            // Remove lightbar/player/reset and brightness/setup validity only.
            // Haptics, speaker audio, microphone LED and adaptive triggers pass through.
            report[2] &= unchecked((byte)~0x1c);
            report[39] &= unchecked((byte)~0x03);
            return ReportAction.BackgroundGuard;
        }
        if (!enabled) return ReportAction.Unchanged;

        report[2] = (byte)((report[2] & ~0x08) | 0x14);
        report[39] |= 0x03; // Brightness and setup; preserve the improved-rumble bit.
        report[42] = 0x02; // Cancel the controller's boot-time blue pulse.
        report[43] = 0;    // Full hardware brightness; RGB handles the user's brightness.
        report[44] = (byte)(frame.PlayerLights & 0x1f);
        report[45] = frame.Red;
        report[46] = frame.Green;
        report[47] = frame.Blue;
        return ReportAction.CustomLighting;
    }

    public static byte[] Create(int length, LightingFrame frame)
    {
        if (!IsSupportedLength(length)) throw new ArgumentOutOfRangeException(nameof(length));
        var report = new byte[length];
        report[0] = 0x02;
        Apply(report, true, true, frame);
        return report;
    }
}
