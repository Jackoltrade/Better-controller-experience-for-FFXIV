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
                Core.DualSenseReport.IsSupportedLength(caps.OutputReportLength);
        }
        finally { HidD_FreePreparsedData(data); }
    }
}
