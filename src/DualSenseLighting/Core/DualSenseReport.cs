// SPDX-License-Identifier: GPL-3.0-only
// Lighting offsets/flags adapted from DS4Windows DualSenseDevice.
// Original DS4Windows portions Copyright (C) 2023 Travis Nickles.
// See THIRD-PARTY-NOTICES.txt for the pinned upstream source.
#nullable enable
using System;

namespace DualSenseLighting.Core;

public enum ReportAction { Unchanged, CustomLighting, BackgroundGuard }

public static class DualSenseReport
{
    public const int BluetoothLength = 78;
    public static bool IsUsbLength(int length) => length is 48 or 63 or 64;
    public static bool IsSupportedLength(int length) => IsUsbLength(length) || length == BluetoothLength;

    // HID caps describe the largest report, which may exceed report 0x31 on Bluetooth.
    public static int ReportLength(int inputLength, int outputLength) =>
        inputLength == 78 && outputLength is >= BluetoothLength and <= 1024 ? BluetoothLength :
        inputLength == 64 && IsUsbLength(outputLength) ? outputLength : 0;

    private static int CommonOffset(ReadOnlySpan<byte> report)
    {
        if (IsUsbLength(report.Length) && report[0] == 2) return 1;
        if (report.Length != BluetoothLength || report[0] != 0x31 || !HasValidChecksum(report)) return -1;
        // Standard sequence/tag header, and the legacy HID-data tag used by DS4Windows.
        if (report[2] == 0x10 && (report[1] & 15) == 0) return 3;
        return report[1] == 2 ? 2 : -1;
    }

    public static ReportAction Apply(Span<byte> report, bool foreground, bool enabled, LightingFrame frame)
    {
        var offset = CommonOffset(report);
        if (offset < 0) return ReportAction.Unchanged;
        var common = report.Slice(offset, 47);
        if (!foreground)
        {
            // Remove lightbar/player/reset and brightness/setup validity only.
            // Haptics, speaker audio, microphone LED and adaptive triggers pass through.
            common[1] &= unchecked((byte)~0x1c);
            common[38] &= unchecked((byte)~0x03);
            if (report.Length == BluetoothLength) SetChecksum(report);
            return ReportAction.BackgroundGuard;
        }
        if (!enabled) return ReportAction.Unchanged;

        common[1] = (byte)((common[1] & ~0x08) | 0x14);
        common[38] |= 0x03; // Brightness and setup; preserve the improved-rumble bit.
        common[41] = 0x02; // Cancel the controller's boot-time blue pulse.
        common[42] = 0; // Full white-LED brightness; RGB brightness is separate.
        // DS4Windows documents bit 0x20 here as an immediate update, bypassing fade-in.
        // Animated white LEDs bypass bank-wide fading so intended steady LEDs do not flicker.
        common[43] = (byte)((frame.PlayerLights & 0x1f) | (frame.PlayerLightsImmediate ? 0x20 : 0));
        common[44] = frame.Red;
        common[45] = frame.Green;
        common[46] = frame.Blue;
        if (report.Length == BluetoothLength) SetChecksum(report);
        return ReportAction.CustomLighting;
    }

    public static byte[] Create(int length, LightingFrame frame) => Create(length, frame, null, 0);

    public static byte[] Create(int length, LightingFrame? frame, RumbleMotors? rumble, byte sequence, bool improvedRumble = false)
    {
        if (!IsSupportedLength(length)) throw new ArgumentOutOfRangeException(nameof(length));
        var report = new byte[length];
        var bluetooth = length == BluetoothLength;
        report[0] = bluetooth ? (byte)0x31 : (byte)2;
        if (bluetooth)
        {
            report[1] = (byte)((sequence & 15) << 4);
            report[2] = 0x10;
            SetChecksum(report);
        }
        if (frame.HasValue) Apply(report, true, true, frame.Value);
        if (rumble.HasValue)
        {
            if (!bluetooth) throw new ArgumentException("Direct rumble is Bluetooth-only.", nameof(rumble));
            var common = report.AsSpan(3, 47);
            common[0] = improvedRumble ? (byte)2 : (byte)3;
            if (improvedRumble) common[38] |= 4; // Firmware-supported compatible vibration v2.
            common[2] = (byte)(rumble.Value.High >> 8);
            common[3] = (byte)(rumble.Value.Low >> 8);
            SetChecksum(report);
        }
        return report;
    }

    // IEEE CRC-32, seeded with the HID output transaction byte 0xA2.
    private static uint Checksum(ReadOnlySpan<byte> report, byte seed = 0xA2)
    {
        uint crc = uint.MaxValue;
        static uint Byte(uint crc, byte value)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            return crc;
        }
        crc = Byte(crc, seed);
        foreach (var value in report[..^4]) crc = Byte(crc, value);
        return ~crc;
    }

    public static bool HasValidChecksum(ReadOnlySpan<byte> report) => report.Length == BluetoothLength &&
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(report[^4..]) == Checksum(report);

    public static void SetChecksum(Span<byte> report)
    {
        if (report.Length != BluetoothLength) throw new ArgumentOutOfRangeException(nameof(report));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(report[^4..], Checksum(report));
    }

    public static bool SupportsImprovedRumble(ushort product, ReadOnlySpan<byte> firmware)
    {
        if (product == 0x0df2) return true; // DualSense Edge supports v2 from launch.
        if (product != 0x0ce6 || firmware.Length != 64 || firmware[0] != 0x20 ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(firmware[^4..]) != Checksum(firmware, 0xA3)) return false;
        // Sony/Linux capability policy: feature version 2.21 introduced vibration v2.
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(firmware[44..]) >= 0x0215;
    }
}
