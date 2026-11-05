// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DualSenseLighting.Windows;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct HidAttributes { public uint Size; public ushort Vendor, Product, Version; }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct HidCaps
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort UsagePage;
        [FieldOffset(4)] public ushort InputReportLength;
        [FieldOffset(6)] public ushort OutputReportLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInterface
    {
        public uint Size;
        public Guid ClassGuid;
        public uint Flags;
        public nint Reserved;
    }

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("hid.dll")]
    internal static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool HidD_GetAttributes(nint handle, ref HidAttributes attributes);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(nint handle, out nint data);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(nint data, out HidCaps caps);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(nint handle, [In, Out] byte[] report, int length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint SetupDiGetClassDevsW(ref Guid guid, nint enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(nint set, nint info, ref Guid guid, uint index, ref DeviceInterface data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetailW(nint set, ref DeviceInterface data, nint detail, uint size, out uint required, nint info);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(nint set);

    // The high bit is the current physical state; the low "pressed since" bit
    // is shared with other processes and cannot determine key ownership.
    internal static bool IsEscapeDown() => (GetAsyncKeyState(0x1B) & 0x8000) != 0;

    internal static bool IsCurrentProcessForeground()
    {
        var window = GetForegroundWindow();
        if (window == 0) return false;
        return GetWindowThreadProcessId(window, out var processId) != 0 && processId == (uint)Environment.ProcessId;
    }

    internal static bool TryGetDualSenseCaps(nint handle, out HidCaps caps)
    {
        caps = default;
        var attributes = new HidAttributes { Size = (uint)Marshal.SizeOf<HidAttributes>() };
        if (!HidD_GetAttributes(handle, ref attributes) || attributes.Vendor != 0x054c ||
            attributes.Product is not (0x0ce6 or 0x0df2)) return false;
        if (!HidD_GetPreparsedData(handle, out var data)) return false;
        try
        {
            return HidP_GetCaps(data, out caps) == 0x00110000 && caps.UsagePage == 1 && caps.Usage == 5 &&
                Core.DualSenseReport.ReportLength(caps.InputReportLength, caps.OutputReportLength) != 0;
        }
        finally { HidD_FreePreparsedData(data); }
    }

    internal static bool SupportsImprovedBluetoothRumble(nint handle)
    {
        var attributes = new HidAttributes { Size = (uint)Marshal.SizeOf<HidAttributes>() };
        if (!HidD_GetAttributes(handle, ref attributes) || attributes.Vendor != 0x054c) return false;
        if (attributes.Product == 0x0df2) return true;
        var firmware = new byte[64]; firmware[0] = 0x20;
        return HidD_GetFeature(handle, firmware, firmware.Length) &&
            Core.DualSenseReport.SupportsImprovedRumble(attributes.Product, firmware);
    }
}
