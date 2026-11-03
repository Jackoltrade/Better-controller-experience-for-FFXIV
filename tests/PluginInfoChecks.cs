// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using DualSenseLighting.Core;

internal static class PluginInfoChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var repository = new Uri(PluginInfo.RepositoryUrl);
        check(repository.Scheme == "https" && repository.Host == "github.com" &&
            repository.AbsolutePath == "/Jackoltrade/PS5-DualSense-Controller-for-FFXIV-", "About source link points to the wrong project");
        var feedback = new Uri(PluginInfo.FeedbackUrl);
        check(feedback.Scheme == "mailto" && feedback.GetLeftPart(UriPartial.Path) == "mailto:" + PluginInfo.FeedbackEmail,
            "Feedback email has the wrong recipient");
        check(Uri.UnescapeDataString(feedback.Query) == "?subject=" + PluginInfo.FeedbackSubject &&
            PluginInfo.FeedbackSubject.Contains(PluginInfo.Name, StringComparison.Ordinal),
            "Feedback subject must identify this plugin and survive URL encoding");
        check(!feedback.Query.Contains('&'), "Feedback URL unexpectedly adds another mail field");
        foreach (var invalid in new string?[] { null, "", "not a URL", "http://ko-fi.com/example", "javascript:alert(1)",
            "file:///C:/example", "https://user:password@example.com/profile" })
            check(!PluginInfo.IsSupportLinkAvailable(invalid), "An unavailable support link should not be clickable");
        check(PluginInfo.IsSupportLinkAvailable("https://ko-fi.com/example") &&
            PluginInfo.IsSupportLinkAvailable("https://afdian.com/a/example"), "Valid HTTPS support profiles should be accepted");
        check(PluginInfo.License == "GPL-3.0-only" && !string.IsNullOrWhiteSpace(PluginInfo.Author),
            "About must retain author and license attribution");
    }
}
