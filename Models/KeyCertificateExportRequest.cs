namespace MoveItAutomationUI.Models;

public sealed class KeyCertificateExportRequest
{
    public string RowId { get; set; } = string.Empty;
    public bool IncludePrivateKey { get; set; }
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}
