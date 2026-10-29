// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Services.MoveIt;

// Centralizes MOVEit license-capacity advisory rules so Dashboard, Tasks, and Hosts cannot drift.
// Capacity advisories are visible to every authenticated MAUI user because the same limits can block
// creation/import operations for non-admin users and the UI must explain why those actions are unavailable.
public sealed record MoveItLicenseCapacityAdvisory(
    string Severity,
    string Resource,
    int Current,
    int Maximum,
    int Remaining,
    string Message);

public static class MoveItLicenseCapacity
{
    private const int WarningThreshold = 25;
    private const int UnlimitedValue = int.MaxValue;

    // Purpose: Returns a Task license-capacity advisory when the configured limit is relevant.
    public static MoveItLicenseCapacityAdvisory? ForTasks(MoveItSessionState? session, int current)
        => Build(current, session?.Info?.License.MaxTasks, "Task", "Tasks");

    // Purpose: Returns a Host license-capacity advisory when the configured limit is relevant.
    public static MoveItLicenseCapacityAdvisory? ForHosts(MoveItSessionState? session, int current)
        => Build(current, session?.Info?.License.MaxHosts, "Host", "Hosts");

    // Purpose: Builds a license-capacity advisory from current usage and licensed limits.
    private static MoveItLicenseCapacityAdvisory? Build(
        int current,
        int? maximumValue,
        string singular,
        string plural)
    {
        if (!maximumValue.HasValue || maximumValue.Value <= 0 || maximumValue.Value == UnlimitedValue) return null;

        var maximum = maximumValue.Value;
        var normalizedCurrent = Math.Max(0, current);
        var remaining = maximum - normalizedCurrent;

        if (normalizedCurrent >= maximum)
        {
            var exceeded = normalizedCurrent > maximum;
            var title = exceeded ? $"{singular} license limit exceeded" : $"{singular} license limit reached";
            var message = $"{title}: {normalizedCurrent:N0} of {maximum:N0} licensed {plural} are in use. Creating additional {plural} is disabled.";
            return new MoveItLicenseCapacityAdvisory("danger", singular, normalizedCurrent, maximum, Math.Max(0, remaining), message);
        }

        if (remaining <= WarningThreshold)
        {
            var slotWord = remaining == 1 ? "slot" : "slots";
            var remainWord = remaining == 1 ? "remains" : "remain";
            var message = $"{singular} license capacity warning: {normalizedCurrent:N0} of {maximum:N0} licensed {plural} are in use. Only {remaining:N0} {singular} {slotWord} {remainWord}.";
            return new MoveItLicenseCapacityAdvisory("warning", singular, normalizedCurrent, maximum, remaining, message);
        }

        return null;
    }
}
