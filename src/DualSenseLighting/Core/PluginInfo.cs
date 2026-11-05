// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;

namespace DualSenseLighting.Core;

public static class PluginInfo
{
    public const string Name = "Better controller experience for FFXIV";
    public const string Command = "/bcef";
    public const string ChatPrefix = "[BCEF] ";
    public const string Author = "Jackoltrade";
    public const string License = "GPL-3.0-only";
    public const string RepositoryUrl = "https://github.com/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-";
    public const string GitHubFeedbackUrl = RepositoryUrl + "/issues/new";
    public const string FeedbackEmail = "jackzhou.work@protonmail.com";
    public const string FeedbackSubject = "[" + Name + "] Feedback";
    public static string FeedbackUrl { get; } = "mailto:" + FeedbackEmail + "?subject=" + Uri.EscapeDataString(FeedbackSubject);

    // Unset support profiles stay disabled until supplied by the maintainer.
    public static readonly string? AfdianUrl = "https://afdian.com/a/jackoltrade";
    public static readonly string? KofiUrl = "https://ko-fi.com/K7C122MEWU";
    public static bool AfdianAvailable { get; } = IsSupportLinkAvailable(AfdianUrl);
    public static bool KofiAvailable { get; } = IsSupportLinkAvailable(KofiUrl);

    public static bool IsSupportLinkAvailable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
}
