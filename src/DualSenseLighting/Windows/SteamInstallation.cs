// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.IO;
using Microsoft.Win32;

namespace DualSenseLighting.Windows;

internal static class SteamInstallation
{
    internal static string GetRoot()
    {
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return steam?.GetValue("SteamPath") as string
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
    }

    internal static string? GetActiveAccount()
    {
        using var process = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
        return process?.GetValue("ActiveUser") is int account && account != 0
            ? unchecked((uint)account).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }
}
