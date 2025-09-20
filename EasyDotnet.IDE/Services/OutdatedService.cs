using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotNetOutdated.Core;
using DotNetOutdated.Core.Models;
using DotNetOutdated.Core.Services;
using NuGet.Versioning;

namespace EasyDotnet.Services;

public class OutdatedService(IProjectAnalysisService projectAnalysisService, INuGetPackageResolutionService nugetService)
{

  public async Task<List<DependencyInfo>> AnalyzeProjectDependenciesAsync(
              string projectPath,
              bool includeTransitive = false,
              int transitiveDepth = 1,
              bool includeUpToDate = false,
              PrereleaseReporting prereleaseReporting = PrereleaseReporting.Auto,
              VersionLock versionLock = VersionLock.None,
              string runtime = "")
  {

    var projects = await projectAnalysisService.AnalyzeProjectAsync(
        projectPath,
        runRestore: false,
        includeTransitive,
        transitiveDepth,
        runtime);

    var dependencyInfos = await Task.WhenAll(
        projects
            .SelectMany(p => p.TargetFrameworks.Select(tf => (Project: p, TargetFramework: tf)))
            .SelectMany(pt => pt.TargetFramework.Dependencies.Values
                .OrderBy(d => d.IsTransitive)
                .ThenBy(d => d.Name)
                .Select(dep => (pt.Project, pt.TargetFramework, Dependency: dep)))
            .Select(async triplet =>
            {
              var info = await AnalyzeDependencyAsync(
              triplet.Project,
              triplet.TargetFramework,
              triplet.Dependency,
              prereleaseReporting,
              versionLock,
              includeUpToDate);
              return info;
            }));

    return [.. dependencyInfos.Where(info => info != null).OfType<DependencyInfo>()];
  }

  private async Task<DependencyInfo?> AnalyzeDependencyAsync(
      Project project,
      TargetFramework targetFramework,
      Dependency dependency,
      PrereleaseReporting prereleaseReporting,
      VersionLock versionLock,
      bool includeUpToDate)
  {
    var referencedVersion = dependency.ResolvedVersion;
    if (referencedVersion is null)
    {
      return null;
    }

    var latestVersion = await nugetService.ResolvePackageVersions(
        dependency.Name,
        referencedVersion,
        project.Sources,
        dependency.VersionRange,
        versionLock,
        prereleaseReporting,
        prereleaseLabel: string.Empty,
        targetFramework.Name,
        project.FilePath,
        dependency.IsDevelopmentDependency,
        olderThanDays: 0,
        ignoreFailedSources: false);

    var isOutdated = latestVersion is not null && referencedVersion != latestVersion;

    return !isOutdated && !includeUpToDate
      ? null
      : new DependencyInfo
      {
        Name = dependency.Name,
        CurrentVersion = referencedVersion.ToString(),
        LatestVersion = latestVersion?.ToString() ?? "Unknown",
        TargetFramework = targetFramework.Name.ToString(),
        IsOutdated = isOutdated,
        IsTransitive = dependency.IsTransitive,
        UpgradeSeverity = GetUpgradeSeverity(referencedVersion, latestVersion)
      };
  }

  private static string GetUpgradeSeverity(NuGetVersion? current, NuGetVersion? latest) =>
    (current, latest) switch
    {
      (null, _) or (_, null) => "None",
      var (c, l) when c.Equals(l) => "None",
      var (c, l) when c.Major != l.Major => "Major",
      var (c, l) when c.Minor != l.Minor => "Minor",
      var (c, l) when c.Patch != l.Patch => "Patch",
      _ => "Unknown"
    };

  public class DependencyInfo
  {
    public required string Name { get; init; }
    public required string CurrentVersion { get; init; }
    public required string LatestVersion { get; init; }
    public required string TargetFramework { get; init; }
    public required bool IsOutdated { get; init; }
    public required bool IsTransitive { get; init; }
    public required string UpgradeSeverity { get; init; }
  }
}