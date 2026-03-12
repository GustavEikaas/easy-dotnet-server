namespace EasyDotnet.Infrastructure;

public sealed record StartupProjectSelection(string ProjectPath, string TargetFramework)
{
  public string Label => $"{Path.GetFileNameWithoutExtension(ProjectPath)}@{TargetFramework}";
}

public abstract record StartupProjectResolutionResult
{
  public sealed record Resolved(StartupProjectSelection Selection) : StartupProjectResolutionResult;
  public sealed record NeedsSelection(List<StartupProjectSelection> Options) : StartupProjectResolutionResult;
}