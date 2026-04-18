namespace EasyDotnet.IDE.Upgrade.Models;

public sealed record UpgradeCandidate(
    string PackageId,
    string CurrentVersion,
    string LatestSafeVersion,
    string LatestVersion,
    string UpgradeSeverity,
    string[] AffectedProjects,
    bool IsCentrallyManaged
);