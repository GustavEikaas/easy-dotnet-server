using DotNetOutdated.Core;
using DotNetOutdated.Core.Services;
using EasyDotnet.IDE.Upgrade.Dispatch;
using EasyDotnet.IDE.Upgrade.Models;
using NuGet.Versioning;

namespace EasyDotnet.IDE.Upgrade.Service;

public sealed class UpgradeAnalysisService(
    IProjectAnalysisService projectAnalysisService,
    INuGetPackageResolutionService nugetResolutionService,
    UpgradeDispatcher dispatcher)
{
  public async Task<UpgradeCandidate[]> AnalyzeAsync(string targetPath, CancellationToken ct)
  {
    await dispatcher.SendStatusAsync(new UpgradeWizardStatus("Analyzing", "Loading projects…"));

    var projects = await projectAnalysisService.AnalyzeProjectAsync(
        targetPath,
        runRestore: false,
        includeTransitiveDependencies: false,
        transitiveDepth: 1,
        runtime: "");

    await dispatcher.SendStatusAsync(new UpgradeWizardStatus("Analyzing", "Resolving versions…"));

    // PackageId → (projectPaths, currentVersion, latestSafe, latestAny, isCentral)
    var byPackage = new Dictionary<string, PackageAccumulator>(StringComparer.OrdinalIgnoreCase);

    var isCentrallyManaged = DetectCentralPackageManagement(targetPath);

    var resolutionTasks = projects
        .SelectMany(p => p.TargetFrameworks.SelectMany(tf =>
            tf.Dependencies.Values
              .Where(d => !d.IsTransitive)
              .Select(dep => (Project: p, TargetFramework: tf, Dependency: dep))))
        .Select(async triplet =>
        {
          var (project, tf, dep) = triplet;

          if (dep.ResolvedVersion is null)
            return ((string PackageId, string ProjectPath, NuGetVersion Current,
                     NuGetVersion? LatestAny, NuGetVersion? LatestSafe)?)null;

          var latestAny = await nugetResolutionService.ResolvePackageVersions(
              dep.Name,
              dep.ResolvedVersion,
              project.Sources,
              dep.VersionRange,
              VersionLock.None,
              PrereleaseReporting.Auto,
              prereleaseLabel: string.Empty,
              tf.Name,
              project.FilePath,
              dep.IsDevelopmentDependency,
              olderThanDays: 0,
              ignoreFailedSources: false);

          var latestSafe = await nugetResolutionService.ResolvePackageVersions(
              dep.Name,
              dep.ResolvedVersion,
              project.Sources,
              dep.VersionRange,
              VersionLock.Major,
              PrereleaseReporting.Auto,
              prereleaseLabel: string.Empty,
              tf.Name,
              project.FilePath,
              dep.IsDevelopmentDependency,
              olderThanDays: 0,
              ignoreFailedSources: false);

          return ((string PackageId, string ProjectPath, NuGetVersion Current,
                   NuGetVersion? LatestAny, NuGetVersion? LatestSafe)?)(
              PackageId: dep.Name,
              ProjectPath: project.FilePath,
              Current: dep.ResolvedVersion,
              LatestAny: latestAny,
              LatestSafe: latestSafe);
        });

    var results = (await Task.WhenAll(resolutionTasks)).Where(r => r.HasValue).Select(r => r!.Value);

    foreach (var r in results)
    {
      if (!byPackage.TryGetValue(r.PackageId, out var acc))
      {
        acc = new PackageAccumulator(r.PackageId, r.Current);
        byPackage[r.PackageId] = acc;
      }
      acc.AddProject(r.ProjectPath);
      acc.MergeVersions(r.LatestAny, r.LatestSafe);
    }

    var candidates = byPackage.Values
        .Where(acc => acc.IsOutdated)
        .Select(acc => acc.ToCandidate(isCentrallyManaged))
        .OrderBy(c => c.UpgradeSeverity == "Major" ? 0 : c.UpgradeSeverity == "Minor" ? 1 : 2)
        .ThenBy(c => c.PackageId, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    await dispatcher.SendInitializedAsync(candidates);
    await dispatcher.SendStatusAsync(new UpgradeWizardStatus("Idle"));

    return candidates;
  }

  private static bool DetectCentralPackageManagement(string targetPath)
  {
    var searchRoot = File.Exists(targetPath)
        ? Path.GetDirectoryName(Path.GetFullPath(targetPath))!
        : Path.GetFullPath(targetPath);

    var dir = new DirectoryInfo(searchRoot);
    while (dir is not null)
    {
      if (dir.GetFiles("Directory.Packages.props").Length > 0)
        return true;
      dir = dir.Parent;
    }
    return false;
  }

  private sealed class PackageAccumulator(string packageId, NuGetVersion current)
  {
    private NuGetVersion? _latestAny;
    private NuGetVersion? _latestSafe;
    private readonly HashSet<string> _projects = new(StringComparer.OrdinalIgnoreCase);

    public bool IsOutdated => _latestAny is not null && current != _latestAny;

    public void AddProject(string path) => _projects.Add(path);

    public void MergeVersions(NuGetVersion? latestAny, NuGetVersion? latestSafe)
    {
      if (latestAny is not null && (_latestAny is null || latestAny > _latestAny))
        _latestAny = latestAny;
      if (latestSafe is not null && (_latestSafe is null || latestSafe > _latestSafe))
        _latestSafe = latestSafe;
    }

    public UpgradeCandidate ToCandidate(bool isCentrallyManaged) => new(
        PackageId: packageId,
        CurrentVersion: current.ToNormalizedString(),
        LatestSafeVersion: (_latestSafe ?? current).ToNormalizedString(),
        LatestVersion: (_latestAny ?? current).ToNormalizedString(),
        UpgradeSeverity: GetSeverity(current, _latestAny),
        AffectedProjects: [.. _projects],
        IsCentrallyManaged: isCentrallyManaged
    );

    private static string GetSeverity(NuGetVersion current, NuGetVersion? latest) =>
        (current, latest) switch
        {
          (_, null) => "None",
          var (c, l) when c.Major != l.Major => "Major",
          var (c, l) when c.Minor != l.Minor => "Minor",
          var (c, l) when c.Patch != l.Patch => "Patch",
          _ => "None"
        };
  }
}
