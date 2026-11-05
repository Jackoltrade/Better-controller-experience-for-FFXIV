// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using DualSenseLighting.Core;

namespace DualSenseLighting.Windows;

internal sealed class GameOutputGuard : IDisposable
{
    private readonly Hook<WriteFileDelegate> writeFile;
    private readonly Hook<SetOutputDelegate> setOutput;
    private readonly Func<LightingSettings> settings;
    private readonly Func<double> seconds;
    private readonly Func<float> sfxLevel;
    private readonly Func<LimitBreakState> limitBreak;
    private long foregroundReports, backgroundReports, errors;

    public long ForegroundReports => Interlocked.Read(ref foregroundReports);
    public long BackgroundReports => Interlocked.Read(ref backgroundReports);
    public long Errors => Interlocked.Read(ref errors);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool WriteFileDelegate(nint file, nint buffer, uint length, nint bytesWritten, nint overlapped);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    private delegate bool SetOutputDelegate(nint file, nint buffer, uint length);

    public GameOutputGuard(IGameInteropProvider interop, Func<LightingSettings> settings, Func<double> seconds, Func<float> sfxLevel,
        Func<LimitBreakState> limitBreak, bool enabled = true)
    {
        this.settings = settings;
        this.seconds = seconds;
        this.sfxLevel = sfxLevel;
        this.limitBreak = limitBreak;
        writeFile = interop.HookFromSymbol<WriteFileDelegate>("kernel32.dll", "WriteFile", WriteFile);
        Hook<SetOutputDelegate>? output = null;
        try
        {
            output = interop.HookFromSymbol<SetOutputDelegate>("hid.dll", "HidD_SetOutputReport", SetOutput);
            setOutput = output;
            SetEnabled(enabled);
        }
        catch { try { ResourceCleanup.Run(() => output?.Dispose(), () => writeFile.Dispose()); } catch { } throw; }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                if (!writeFile.IsEnabled) writeFile.Enable();
                if (!setOutput.IsEnabled) setOutput.Enable();
            }
            else
            {
                if (writeFile.IsEnabled) writeFile.Disable();
                if (setOutput.IsEnabled) setOutput.Disable();
            }
        }
        catch
        {
            try { ResourceCleanup.Run(
                () => { if (writeFile.IsEnabled) writeFile.Disable(); },
                () => { if (setOutput.IsEnabled) setOutput.Disable(); }); } catch { }
            throw;
        }
    }

    private unsafe void Filter(nint file, nint buffer, uint length)
    {
        if (buffer == 0 || !DualSenseReport.IsSupportedLength((int)length)) return;
        if (OwnedLightingHandles.Contains(file) || Marshal.ReadByte(buffer) is not (2 or 0x31)) return;
        // Revalidate handles rather than caching identities across a disconnect/handle reuse.
        if (!Native.TryGetDualSenseCaps(file, out _)) return;
        var snapshot = settings();
        var action = DualSenseReport.Apply(new Span<byte>((void*)buffer, (int)length),
            Native.IsCurrentProcessForeground(), snapshot.Enabled, LightingEffects.Render(snapshot, seconds(), sfxLevel(), limitBreak()));
        if (action == ReportAction.CustomLighting) Interlocked.Increment(ref foregroundReports);
        if (action == ReportAction.BackgroundGuard) Interlocked.Increment(ref backgroundReports);
    }

    private bool WriteFile(nint file, nint buffer, uint length, nint bytesWritten, nint overlapped)
    {
        // Patch the caller's buffer in place: an overlapped write must retain its original lifetime.
        try { Filter(file, buffer, length); } catch { Interlocked.Increment(ref errors); }
        return writeFile.OriginalDisposeSafe(file, buffer, length, bytesWritten, overlapped);
    }

    private bool SetOutput(nint file, nint buffer, uint length)
    {
        try { Filter(file, buffer, length); } catch { Interlocked.Increment(ref errors); }
        return setOutput.OriginalDisposeSafe(file, buffer, length);
    }

    public void Dispose()
    {
        ResourceCleanup.Run(() => setOutput.Dispose(), () => writeFile.Dispose());
    }
}
