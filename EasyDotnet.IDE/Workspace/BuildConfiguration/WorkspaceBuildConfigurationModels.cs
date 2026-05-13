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

public sealed record WorkspaceBuildConfigurationChangedEventArgs(
    string SolutionPath,
    WorkspaceBuildConfiguration Previous,
    WorkspaceBuildConfiguration Current);
