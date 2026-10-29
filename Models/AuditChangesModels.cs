// Project: MAUI - MOVEit Automation UI
namespace MoveItAutomationUI.Models;

public sealed record AuditChangeRecord(string PreChange, string PostChange);
public sealed record AuditChangeLine(string Kind, int? Before, int? After, string Text);
public sealed record AuditChangeComparison(IReadOnlyList<AuditChangeLine> Lines, int Added, int Removed, bool Grouped, bool PlainText);
