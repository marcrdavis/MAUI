namespace MoveItAutomationUI.Services.MoveIt;

public sealed record GlobalParameterEditSnapshot(string Name, string Value, bool Secure, string EditVersion);
