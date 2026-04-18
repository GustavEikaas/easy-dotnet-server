namespace EasyDotnet.IDE.Upgrade.Models;

public sealed record UpgradeResult(
    UpgradeResultItem[] Updated,
    UpgradeResultItem[] Failed
);

public sealed record UpgradeResultItem(
    string PackageId,
    string FromVersion,
    string ToVersion,
    string? Error = null
);
