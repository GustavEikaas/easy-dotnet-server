namespace EasyDotnet.IDE.Upgrade.Models;

public sealed record OpenRequest(string TargetPath);

public sealed record ChangelogRequest(string PackageId, string Version);

public sealed record VersionsRequest(string PackageId);
