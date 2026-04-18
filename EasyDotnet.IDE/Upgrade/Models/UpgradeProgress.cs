namespace EasyDotnet.IDE.Upgrade.Models;

public sealed record UpgradeProgress(
    string PackageId,
    int Current,
    int Total,
    bool Success,
    string? Error = null
);