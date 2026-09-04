using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EasyDotnet.IDE.ProjectView.Models;

[JsonConverter(typeof(StringEnumConverter))]
public enum ProjectViewAction
{
  AddPackage,
  RemovePackage,
  UpdatePackage,
  AddProjectReference,
  RemoveProjectReference,
  Refresh
}

public sealed record ProjectViewSnapshot(
    ProjectViewHeader Header,
    IReadOnlyList<PackageNode> Packages,
    IReadOnlyList<ProjectRefNode> ProjectReferences);

public sealed record ProjectViewStatus(
    string ProjectPath,
    bool IsLoading,
    string? Operation);

public sealed record ProjectViewHeader(
    string ProjectPath,
    string Name,
    string? Version,
    string? LangVersion,
    string? OutputType,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<ProjectViewAction> AvailableActions);

public sealed record PackageNode(
    string Id,
    string Version,
    bool IsOutdated,
    IReadOnlyList<ProjectViewAction> AvailableActions,
    string? LatestVersion = null,
    string? UpgradeSeverity = null);

public sealed record ProjectRefNode(
    string Path,
    string Name,
    IReadOnlyList<ProjectViewAction> AvailableActions);
