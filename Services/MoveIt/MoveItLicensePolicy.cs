// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Services.Concurrency;

namespace MoveItAutomationUI.Services.MoveIt;

// Centralizes MOVEit license feature and capacity rules so UI visibility and server-side enforcement cannot drift.
// AS2 and Backup Hosts are intentionally represented but are not enforced in RC1 until their MAUI behavior is fully understood.
public sealed record MoveItLicenseCapabilities(
    bool AdvancedTasksLicensed,
    bool CustomScriptsLicensed,
    bool PgpLicensed,
    bool As2Licensed,
    bool BackupHostsLicensed,
    int? MaxTasks,
    int? MaxHosts);

public static class MoveItLicensePolicy
{
    private const int UnlimitedValue = int.MaxValue;
    private static readonly KeyedAsyncLock TaskCreationLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly KeyedAsyncLock HostCreationLocks = new(StringComparer.OrdinalIgnoreCase);

    // Returns a permissive snapshot only when /api/v0/info was unavailable; authenticated sessions normally always carry Info.
    // This fallback avoids disabling established MAUI behavior for older/unexpected MOVEit builds that omit the license object.
    public static MoveItLicenseCapabilities GetCapabilities(MoveItSessionState? session)
    {
        var info = session?.Info;
        if (info is null || !info.LicenseInformationAvailable)
        {
            return new MoveItLicenseCapabilities(true, true, true, false, false, null, null);
        }

        var license = info.License;
        return new MoveItLicenseCapabilities(
            license.AdvancedTasksLicensed,
            license.CustomScriptsLicensed && !info.CustomScriptingDisabled,
            license.PgpLicensed,
            license.As2Licensed,
            license.BackupHostsLicensed,
            license.MaxTasks,
            license.MaxHosts);
    }

    public static bool IsAdvancedTaskType(string? taskType)
        => string.Equals((taskType ?? string.Empty).Trim(), "Advanced", StringComparison.OrdinalIgnoreCase);

    public static string? GetAdvancedTaskCreationBlock(MoveItSessionState? session, string? taskType)
    {
        if (!IsAdvancedTaskType(taskType)) return null;
        return GetCapabilities(session).AdvancedTasksLicensed
            ? null
            : "The connected MOVEit Automation license does not include Advanced Tasks. Advanced Task creation is disabled in MAUI.";
    }

    public static string? GetCustomScriptBlock(MoveItSessionState? session)
        => GetCapabilities(session).CustomScriptsLicensed
            ? null
            : "The connected MOVEit Automation license does not include Custom Scripts. Only built-in MOVEit Scripts are available in MAUI.";

    public static string? GetPgpBlock(MoveItSessionState? session)
        => GetCapabilities(session).PgpLicensed
            ? null
            : "The connected MOVEit Automation license does not include PGP. PGP key operations are disabled in MAUI.";

    public static string? GetTaskCapacityBlock(MoveItSessionState? session, int currentCount, int additionalCount = 1)
        => BuildCapacityBlock(GetCapabilities(session).MaxTasks, currentCount, additionalCount, "Task", "Tasks");

    public static string? GetHostCapacityBlock(MoveItSessionState? session, int currentCount, int additionalCount = 1)
        => BuildCapacityBlock(GetCapabilities(session).MaxHosts, currentCount, additionalCount, "Host", "Hosts");

    public static bool IsTaskLimitReached(MoveItSessionState? session, int currentCount)
        => GetTaskCapacityBlock(session, currentCount, 1) is not null;

    public static bool IsHostLimitReached(MoveItSessionState? session, int currentCount)
        => GetHostCapacityBlock(session, currentCount, 1) is not null;

    // Serializes MAUI Task creation/import/clone paths per MOVEit environment so concurrent requests cannot both
    // pass the same capacity check and exceed a finite license limit through MAUI.
    public static ValueTask<IDisposable> AcquireTaskCreationAsync(MoveItSessionState session, CancellationToken cancellationToken = default)
        => TaskCreationLocks.AcquireAsync($"task:{session.GetEnvironmentKey()}", cancellationToken);

    // Serializes MAUI Host creation/import paths per MOVEit environment for the same capacity reason.
    public static ValueTask<IDisposable> AcquireHostCreationAsync(MoveItSessionState session, CancellationToken cancellationToken = default)
        => HostCreationLocks.AcquireAsync($"host:{session.GetEnvironmentKey()}", cancellationToken);

    private static string? BuildCapacityBlock(int? maximumValue, int currentCount, int additionalCount, string singular, string plural)
    {
        if (!IsFiniteLimit(maximumValue) || additionalCount <= 0) return null;
        var maximum = maximumValue!.Value;
        var current = Math.Max(0, currentCount);
        var requested = Math.Max(0, additionalCount);
        if ((long)current + requested <= maximum) return null;

        if (current >= maximum)
        {
            return $"The MOVEit {singular} license limit has been reached ({current:N0} of {maximum:N0}). Creating additional {plural} is disabled.";
        }

        var remaining = Math.Max(0, maximum - current);
        return $"This operation would exceed the MOVEit {singular} license limit. {current:N0} of {maximum:N0} licensed {plural} are in use, leaving {remaining:N0} available; the operation would create {requested:N0}.";
    }

    private static bool IsFiniteLimit(int? value)
        => value.HasValue && value.Value > 0 && value.Value != UnlimitedValue;
}
