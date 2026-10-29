// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Reflection;

namespace MoveItAutomationUI.Models;

public static class MauiReleaseInfo
{
    // The project InformationalVersion is the single release-controlled build source.
    // .NET may append the source revision after the date; neither live settings nor
    // the running server's clock should change the build shown by Footer/About.
    public static string Build { get; } = ReadBuild(typeof(MauiReleaseInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static string ReadBuild(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion)) return "Unknown";
        const string marker = "+build.";
        var markerIndex = informationalVersion.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0) return "Unknown";
        var start = markerIndex + marker.Length;
        var end = informationalVersion.IndexOf('.', start);
        var date = end < 0 ? informationalVersion[start..] : informationalVersion[start..end];
        return date.Length == 8 && DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _) ? date : "Unknown";
    }
}
