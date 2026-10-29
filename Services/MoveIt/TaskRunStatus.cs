// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Services.MoveIt;

// Centralizes the Task Run result labels used by both the Dashboard KPIs and Dashboard-linked reports.
internal static class TaskRunStatus
{
    // Purpose: Normalizes a MOVEit status value into the MAUI task-run status vocabulary.
    public static string Normalize(string value)
    {
        var status = (value ?? string.Empty).Trim();
        if (status.Equals("Failure", StringComparison.OrdinalIgnoreCase) || status.Equals("Failed", StringComparison.OrdinalIgnoreCase)) return "Failed";
        if (status.Equals("Success", StringComparison.OrdinalIgnoreCase) || status.Equals("Successful", StringComparison.OrdinalIgnoreCase)) return "Success";
        if (status.Equals("No xfers", StringComparison.OrdinalIgnoreCase)
            || status.Equals("No Transfer", StringComparison.OrdinalIgnoreCase)
            || status.Equals("No Transfers", StringComparison.OrdinalIgnoreCase)) return "No Transfers";
        return status;
    }
}
