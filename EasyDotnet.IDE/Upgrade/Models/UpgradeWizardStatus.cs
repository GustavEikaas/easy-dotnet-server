namespace EasyDotnet.IDE.Upgrade.Models;

// Phase values: "Idle" | "Analyzing" | "Applying" | "Done" | "Failed"
public sealed record UpgradeWizardStatus(string Phase, string? Message = null);