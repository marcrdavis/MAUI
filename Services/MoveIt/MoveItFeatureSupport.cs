using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Services.MoveIt;

public enum MoveItFeature { OneDriveHosts, AmazonS3IamAuthentication, AuditConfigurationChanges }

// Release-specific capabilities have one minimum-version registry. Always prefer the
// Automation engine version over Web Admin's version; an unknown version grants no new feature.
public static class MoveItFeatureSupport
{
    // Progress publishes both names: release 2026.0 is engine version 18.0.
    // Keep explicit pairs per feature; never derive a calendar year from arbitrary engine numbers.
    private static (Version Release, Version Engine) Minimum(MoveItFeature feature) => feature switch
    {
        MoveItFeature.OneDriveHosts or MoveItFeature.AmazonS3IamAuthentication or MoveItFeature.AuditConfigurationChanges => (new(2026, 0), new(18, 0)),
        _ => throw new ArgumentOutOfRangeException(nameof(feature))
    };
    public static Version MinimumVersion(MoveItFeature feature) => Minimum(feature).Release;

    public static bool Supports(MoveItInfo? info, MoveItFeature feature)
    {
        var text = string.IsNullOrWhiteSpace(info?.CentralVersion) ? info?.AppVersion : info.CentralVersion;
        var match = Regex.Match(text?.Trim() ?? "", @"^(?:MOVEit\s+Automation\s+|v)?(?<v>\d{1,4}(?:\.\d+){0,3})(?:[^\d.]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var number = match.Groups["v"].Value;
        if (!number.Contains('.')) number += ".0";
        if (!match.Success || !Version.TryParse(number, out var version)) return false;
        var minimum = Minimum(feature);
        return version.Major >= 2000 ? version >= minimum.Release : version.Major < 100 && version >= minimum.Engine;
    }

    public static void Require(MoveItInfo? info, MoveItFeature feature)
    {
        if (!Supports(info, feature))
            throw new InvalidOperationException($"{Label(feature)} requires MOVEit Automation {MinimumVersion(feature)} or later. Sign in again if the server was upgraded.");
    }

    private static string Label(MoveItFeature feature) => feature switch
    {
        MoveItFeature.OneDriveHosts => "OneDrive Hosts",
        MoveItFeature.AmazonS3IamAuthentication => "Amazon S3 IAM authentication",
        MoveItFeature.AuditConfigurationChanges => "Audit configuration changes",
        _ => throw new ArgumentOutOfRangeException(nameof(feature))
    };

    internal static void RequireHost(JsonObject payload, MoveItInfo? info)
    {
        var (type, host) = HostDefinitionJson.UnwrapHost(payload);
        if (type.Equals("OneDrive", StringComparison.OrdinalIgnoreCase)) Require(info, MoveItFeature.OneDriveHosts);
        if (type.Equals("S3", StringComparison.OrdinalIgnoreCase) && HostCloudAuthenticationSettings.S3Method(host) != 0)
            Require(info, MoveItFeature.AmazonS3IamAuthentication);
    }
}
