namespace EasyDotnet.IDE.Upgrade.Models;

public sealed record ApplyRequest(string TargetPath, UpgradeSelection[] Selections);

public sealed record UpgradeSelection(
    string PackageId,
    string TargetVersion,
    string CurrentVersion,
    string[] AffectedProjects,
    bool IsCentrallyManaged
);
