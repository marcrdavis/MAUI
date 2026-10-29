// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

/// <summary>
/// Produces the stable JSON row shape consumed by the shared client-side grid replacement API.
/// </summary>
public static class GridClientContract
{
    public static object FromRow(GridRow row) => new
    {
        id = row.Id,
        infoActionUrl = row.InfoActionUrl,
        values = row.Values,
        rawValues = row.RawValues
    };
}
