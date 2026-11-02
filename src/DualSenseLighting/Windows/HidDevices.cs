// SPDX-License-Identifier: GPL-3.0-only
// Shared HID enumeration adapted from HidLibrary in DS4Windows (originally MIT).
// See LICENSE-HidLibrary.txt and THIRD-PARTY-NOTICES.txt.
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

internal sealed class HidDevices : ILightingDevices
{
    public IReadOnlyCollection<string> GetPaths()
    {
        Native.HidD_GetHidGuid(out var guid);
        var set = Native.SetupDiGetClassDevsW(ref guid, 0, 0, 0x12); // PRESENT | DEVICEINTERFACE
        if (set == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        var paths = new List<string>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new Native.DeviceInterface { Size = (uint)Marshal.SizeOf<Native.DeviceInterface>() };
                if (!Native.SetupDiEnumDeviceInterfaces(set, 0, ref guid, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != 259) throw new Win32Exception(error); // NO_MORE_ITEMS
                    break;
                }
                Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, 0, 0, out var size, 0);
                if (size < 6) throw new Win32Exception(Marshal.GetLastWin32Error());
                var detail = Marshal.AllocHGlobal(checked((int)size));
                try
                {
                    Marshal.WriteInt32(detail, nint.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, size, out _, 0))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    var path = Marshal.PtrToStringUni(detail + 4);
                    if (path != null && path.Contains("vid_054c", StringComparison.OrdinalIgnoreCase)) paths.Add(path);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        return paths;
    }

    public ILightingDevice? Open(string path)
    {
        // Shared write access, asynchronous I/O. Never read or hide a controller.
        var handle = Native.CreateFileW(path, 0x40000000, 3, 0, 3, 0x40000000, 0);
        if (handle.IsInvalid) { handle.Dispose(); return null; }
        try
        {
            if (!Native.TryGetDualSenseCaps(handle.DangerousGetHandle(), out var caps)) { handle.Dispose(); return null; }
            var serial = Native.GetControllerSerial(handle.DangerousGetHandle());
            return new HidDevice(new FileStream(handle, FileAccess.Write, 1, true), caps.OutputReportLength, serial);
        }
        catch { handle.Dispose(); throw; }
    }

    private sealed class HidDevice : ILightingDevice
    {
        private readonly FileStream stream;
        private readonly nint handle;
        private int disposed;
        public int ReportLength { get; }
        public string? Serial { get; }

        internal HidDevice(FileStream stream, int reportLength, string? serial)
        {
            this.stream = stream;
            ReportLength = reportLength;
            Serial = serial;
            handle = stream.SafeFileHandle.DangerousGetHandle();
            OwnedLightingHandles.Add(handle);
        }

        public async Task WriteAsync(byte[] report, CancellationToken cancellationToken) =>
            await stream.WriteAsync(report.AsMemory(), cancellationToken).ConfigureAwait(false);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            // Remove before closing to avoid matching a handle subsequently reused by the game.
            OwnedLightingHandles.Remove(handle);
            stream.Dispose();
        }
    }
}
