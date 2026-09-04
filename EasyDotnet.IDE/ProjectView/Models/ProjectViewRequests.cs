namespace EasyDotnet.IDE.ProjectView.Models;

public sealed record ProjectViewRequest(string ProjectPath);

public sealed record ProjectViewGetRequest(string? ProjectPath = null);

public sealed record ProjectViewPackageRequest(string ProjectPath, string PackageId);

public sealed record ProjectViewUpgradeRequest(string ProjectPath, string PackageId, string Version);

public sealed record ProjectViewProjectRefRequest(string ProjectPath, string TargetPath);
