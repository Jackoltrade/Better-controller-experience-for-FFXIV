// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

// API and ABI: https://learn.microsoft.com/en-us/windows/win32/api/xinput/
/// <summary>Blends plugin rumble with the game's latest motor request. No inputs are read.</summary>
internal sealed unsafe class XInputRumble : IRumbleDevices, IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vibration { public ushort Low, High; }
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct Capabilities { [FieldOffset(0)] public byte Type; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint SetState(uint slot, Vibration* vibration);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetCapabilities")]
    private static extern uint GetCapabilities(uint slot, uint flags, out Capabilities caps);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string name);

    private readonly object sync = new();
    private readonly RumbleMotors[] game = new RumbleMotors[4];
    private readonly RumbleMotors[] plugin = new RumbleMotors[4];
    private readonly RumbleMotors[] screen = new RumbleMotors[4];
    private readonly List<Hook<SetState>> hooks = new();
    private nint module;
    private readonly IGameInteropProvider interop;
    [ThreadStatic] private static bool forwarding;
    private readonly Func<bool> foreground;
    private bool disposed;

    public XInputRumble(IGameInteropProvider interop, Func<bool> foreground)
    {
        this.foreground = foreground;
        this.interop = interop;
    }

    private void Initialize()
    {
        if (module != 0) return;
        module = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "xinput1_4.dll"));
        try
        {
            var addresses = new HashSet<nint>();
            // Hook the system API we use and the legacy versions already loaded by
            // the game. Deduplicate forwarded exports. Never install a DLL or driver.
            foreach (var library in new[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" })
            {
                var handle = GetModuleHandleW(library);
                if (handle == 0 || !NativeLibrary.TryGetExport(handle, "XInputSetState", out var address) || !addresses.Add(address)) continue;
                Hook<SetState>? hook = null;
                hook = interop.HookFromAddress<SetState>(address, (index, vibration) => ApplyGame(hook!, index, vibration));
                hooks.Add(hook);
            }
            if (hooks.Count == 0) throw new NotSupportedException("XInput vibration is unavailable.");
            foreach (var hook in hooks) hook.Enable();
        }
        catch
        {
            try { ReleaseHooks(); } catch { }
            throw;
        }
    }

    public bool IsConnected(int slot)
    {
        Initialize();
        if (slot is < 0 or > 3) return false;
        var connected = GetCapabilities((uint)slot, 1, out var caps) == 0 && caps.Type == 1;
        if (!connected) lock (sync) { game[slot] = default; plugin[slot] = default; screen[slot] = default; }
        return connected;
    }

    private uint ApplyGame(Hook<SetState> hook, uint slot, Vibration* vibration)
    {
        if (forwarding || slot >= 4 || vibration == null) return hook.OriginalDisposeSafe(slot, vibration);
        lock (sync)
        {
            game[slot] = new(vibration->Low, vibration->High);
            var allowed = !disposed && foreground();
            var merged = RumbleMotors.Merge(game[slot], allowed ? plugin[slot] : default, allowed ? screen[slot] : default);
            var output = new Vibration { Low = merged.Low, High = merged.High };
            forwarding = true;
            try { return hook.OriginalDisposeSafe(slot, &output); }
            finally { forwarding = false; }
        }
    }

    public bool SetVibration(int slot, RumbleMotors motors) => SetVibration(slot, motors, default);

    public bool SetVibration(int slot, RumbleMotors motors, RumbleMotors screenMotors)
    {
        if (slot is < 0 or > 3) return false;
        lock (sync)
        {
            if (disposed) return false;
            var allowed = foreground();
            plugin[slot] = allowed ? motors : default;
            screen[slot] = allowed ? screenMotors : default;
            var merged = RumbleMotors.Merge(game[slot], plugin[slot], screen[slot]);
            var output = new Vibration { Low = merged.Low, High = merged.High };
            uint result;
            forwarding = true;
            try { result = hooks[0].OriginalDisposeSafe((uint)slot, &output); }
            finally { forwarding = false; }
            if (result != 0) { game[slot] = default; plugin[slot] = default; screen[slot] = default; }
            return result == 0;
        }
    }

    private void ReleaseHooks()
    {
        var failures = new List<Exception>();
        foreach (var hook in hooks)
            try { hook.Dispose(); } catch (Exception error) { failures.Add(error); }
        hooks.Clear();
        if (module != 0)
        {
            try { NativeLibrary.Free(module); } catch (Exception error) { failures.Add(error); }
            finally { module = 0; }
        }
        if (failures.Count != 0) throw new AggregateException("XInput hooks could not be released.", failures);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            ResourceCleanup.Run(() =>
            {
                var failures = new List<Exception>();
                try
                {
                    for (var i = 0; i < 4; i++)
                        try { if (plugin[i] != default || screen[i] != default) SetVibration(i, default); }
                        catch (Exception error) { failures.Add(error); }
                }
                finally { disposed = true; }
                if (failures.Count != 0) throw new AggregateException(failures);
            }, ReleaseHooks);
        }
    }
}
