namespace EasyDotnet.IDE.Workspace.BuildConfiguration;

public sealed record WorkspaceBuildConfiguration(string BuildType, string? Platform)
{
  public string DisplayName => string.IsNullOrWhiteSpace(Platform) ? BuildType : $"{BuildType}|{Platform}";
}

public sealed record ResolvedBuildConfiguration(
    string TargetPath,
    WorkspaceBuildConfiguration WorkspaceConfiguration,
    string Configuration,
    string? Platform,
    bool Build,
    bool Deploy,
    bool UsedProjectMapping);

public static class WorkspaceBuildConfigurationDisplay
{
  public static string ToDisplayName(
      WorkspaceBuildConfiguration configuration,
      IReadOnlyList<WorkspaceBuildConfiguration> available)
  {
    var platforms = available
        .Select(x => x.Platform)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    return platforms > 1 ? configuration.DisplayName : configuration.BuildType;
  }
}

public sealed record WorkspaceBuildConfigurationChangedEventArgs(
    string SolutionPath,
    WorkspaceBuildConfiguration Previous,
    WorkspaceBuildConfiguration Current);