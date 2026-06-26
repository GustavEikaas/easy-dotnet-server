using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Solution;
using EasyDotnet.IDE.PackageManager;
using EasyDotnet.IDE.Picker.Models;
using EasyDotnet.IDE.ProjectView.Models;
using EasyDotnet.IDE.ProjectReference.Services;
using EasyDotnet.Services;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using StreamJsonRpc;

namespace EasyDotnet.IDE.ProjectView.Services;

public class ProjectViewService(
    IBuildHostManager buildHostManager,
    PackageManagerService packageManagerService,
    ProjectReferenceService projectReferenceService,
    ProjectReferenceCliService projectReferenceCliService,
    NugetService nugetService,
    OutdatedService outdatedService,
    IClientService clientService,
    ISolutionService solutionService,
    IProcessQueue processQueue,
    IEditorService editorService,
    JsonRpc rpc,
    ILogger<ProjectViewService> logger)
{
  private const int ProjectSearchDepth = 3;

  private static readonly IReadOnlyList<ProjectViewAction> HeaderActions =
      [ProjectViewAction.AddPackage, ProjectViewAction.AddProjectReference, ProjectViewAction.Refresh];

  private static readonly IReadOnlyList<ProjectViewAction> PackageActions =
      [ProjectViewAction.RemovePackage, ProjectViewAction.UpdatePackage];

  private static readonly IReadOnlyList<ProjectViewAction> ProjectRefActions =
      [ProjectViewAction.RemoveProjectReference];

  private sealed record OutdatedInfo(string LatestVersion, string Severity);

  public async Task<ProjectViewSnapshot?> OpenAsync(string? projectPath, CancellationToken ct)
  {
    var resolved = await ResolveProjectAsync(projectPath, ct);
    return resolved is null ? null : await GetSnapshotAsync(resolved, outdated: null, ct);
  }

  private async Task<string?> ResolveProjectAsync(string? projectPath, CancellationToken ct)
  {
    if (!string.IsNullOrWhiteSpace(projectPath))
    {
      return Path.GetFullPath(projectPath);
    }

    var solutionFile = clientService.ProjectInfo?.SolutionFile;

    List<string> projectPaths;
    if (solutionFile is not null)
    {
      var solutionProjects = await solutionService.GetProjectsFromSolutionFile(solutionFile, ct);
      projectPaths = [.. solutionProjects.OnlyDotnetProjects().Select(p => p.AbsolutePath)];
    }
    else
    {
      var rootDir = clientService.RequireRootDir();
      projectPaths = [.. Directory.EnumerateFiles(rootDir, "*.csproj", new EnumerationOptions
      {
        MaxRecursionDepth = ProjectSearchDepth,
        RecurseSubdirectories = true,
      })];
    }

    if (projectPaths.Count == 0)
    {
      await editorService.DisplayError("No .NET projects found");
      return null;
    }

    if (projectPaths.Count == 1)
    {
      return Path.GetFullPath(projectPaths[0]);
    }

    var choices = projectPaths
        .Select(p => new PickerChoice<string>(p, Path.GetFileNameWithoutExtension(p), p))
        .ToArray();

    var picked = await editorService.RequestPickerAsync("Open project view for", choices, ct: ct);
    return picked is null ? null : Path.GetFullPath(picked);
  }

  public Task<ProjectViewSnapshot> GetSnapshotAsync(string projectPath, CancellationToken ct) =>
      GetSnapshotAsync(projectPath, outdated: null, ct);

  private async Task<ProjectViewSnapshot> GetSnapshotAsync(string projectPath, IReadOnlyDictionary<string, OutdatedInfo>? outdated, CancellationToken ct)
  {
    var fullPath = Path.GetFullPath(projectPath);

    var evaluations = await buildHostManager
        .GetProjectPropertiesBatchAsync(new GetProjectPropertiesBatchRequest([fullPath], null), ct)
        .ToListAsync(ct);

    var projects = evaluations
        .Where(e => e.Success && e.Project is not null)
        .Select(e => e.Project!)
        .ToList();

    var primary = projects.FirstOrDefault();

    var targetFrameworks = evaluations
        .Select(e => e.TargetFramework)
        .Concat(projects.Select(p => p.TargetFramework))
        .Where(tfm => !string.IsNullOrWhiteSpace(tfm))
        .Select(tfm => tfm!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var header = new ProjectViewHeader(
        ProjectPath: fullPath,
        Name: primary?.ProjectName ?? Path.GetFileNameWithoutExtension(fullPath),
        Version: primary?.Raw.Version,
        LangVersion: primary?.Raw.LangVersion,
        OutputType: primary?.OutputType,
        TargetFrameworks: targetFrameworks,
        AvailableActions: HeaderActions);

    var installed = await buildHostManager.ListPackageReferencesAsync(fullPath, ct);
    var packages = installed
        .Select(p =>
        {
          var upgrade = outdated is not null && outdated.TryGetValue(p.Id, out var info) ? info : null;
          return new PackageNode(
              p.Id,
              p.Version,
              IsOutdated: upgrade is not null,
              PackageActions,
              LatestVersion: upgrade?.LatestVersion,
              UpgradeSeverity: upgrade?.Severity);
        })
        .ToList();

    var refs = await projectReferenceCliService.GetProjectReferencesAsync(fullPath, ct);
    var projectReferences = refs
        .Select(r => new ProjectRefNode(r, Path.GetFileNameWithoutExtension(r) ?? r, ProjectRefActions))
        .ToList();

    return new ProjectViewSnapshot(header, packages, projectReferences);
  }

  public Task AddPackageAsync(string projectPath, CancellationToken ct) =>
      RunOperationAsync(projectPath, "Adding package…", c => packageManagerService.AddPackageAsync(new AddPackageRequest(projectPath), c), ct);

  public Task RemovePackageAsync(string projectPath, string packageId, CancellationToken ct) =>
      RunOperationAsync(projectPath, $"Removing {packageId}…", c => packageManagerService.RemovePackageAsync(new RemovePackageRequest(projectPath, [packageId]), c), ct);

  public Task UpdatePackageAsync(string projectPath, string packageId, CancellationToken ct) =>
      RunOperationAsync(projectPath, $"Updating {packageId}…", c => UpdatePackageCoreAsync(projectPath, packageId, c), ct);

  public Task UpgradePackageAsync(string projectPath, string packageId, string version, CancellationToken ct) =>
      RunOperationAsync(projectPath, $"Upgrading {packageId} to {version}…", c => ApplyPackageVersionAsync(Path.GetFullPath(projectPath), packageId, version, c), ct);

  public Task CheckOutdatedAsync(string projectPath, CancellationToken ct) =>
      RunOperationAsync(projectPath, "Checking for updates…", async c =>
      {
        var outdated = await ComputeOutdatedAsync(projectPath, c);
        var snapshot = await GetSnapshotAsync(projectPath, outdated, c);
        await rpc.NotifyWithParameterObjectAsync("projectview/update", snapshot);
      }, ct, refreshAfter: false);

  public Task UpgradeAllOutdatedAsync(string projectPath, CancellationToken ct) =>
      RunOperationAsync(projectPath, "Upgrading all packages…", c => UpgradeAllOutdatedCoreAsync(projectPath, c), ct);

  private async Task UpgradeAllOutdatedCoreAsync(string projectPath, CancellationToken ct)
  {
    var fullPath = Path.GetFullPath(projectPath);
    var outdated = await ComputeOutdatedAsync(projectPath, ct);
    if (outdated.Count == 0)
    {
      await editorService.DisplayMessage("All packages are up to date");
      return;
    }

    foreach (var (packageId, info) in outdated)
    {
      var (success, _, err) = await processQueue.RunProcessAsync(
          "dotnet",
          $"add \"{fullPath}\" package \"{packageId}\" --version \"{info.LatestVersion}\"",
          new ProcessOptions(KillOnTimeout: true),
          ct);

      if (!success)
      {
        logger.LogError("dotnet add package (upgrade-all) failed for {pkg}: {err}", packageId, err);
        await editorService.DisplayError($"Failed to upgrade {packageId}: {err}");
        return;
      }
    }

    if (await RestoreAndReportAsync(fullPath, ct))
    {
      await editorService.DisplayMessage($"Upgraded {outdated.Count} package(s)");
    }
  }

  private async Task<IReadOnlyDictionary<string, OutdatedInfo>> ComputeOutdatedAsync(string projectPath, CancellationToken ct)
  {
    var deps = await outdatedService.AnalyzeProjectDependenciesAsync(Path.GetFullPath(projectPath), includeTransitive: false);
    var map = new Dictionary<string, OutdatedInfo>(StringComparer.OrdinalIgnoreCase);
    foreach (var dep in deps)
    {
      if (dep is { IsOutdated: true, IsTransitive: false } && !map.ContainsKey(dep.Name))
      {
        map[dep.Name] = new OutdatedInfo(dep.LatestVersion, dep.UpgradeSeverity);
      }
    }
    return map;
  }

  public Task AddProjectReferenceAsync(string projectPath, CancellationToken ct) =>
      RunOperationAsync(projectPath, "Adding project reference…", c => projectReferenceService.AddProjectReferenceInteractiveAsync(projectPath, c), ct);

  public Task RemoveProjectReferenceAsync(string projectPath, string targetPath, CancellationToken ct) =>
      RunOperationAsync(projectPath, "Removing project reference…", c => RemoveProjectReferenceCoreAsync(projectPath, targetPath, c), ct);

  public Task RefreshAsync(string projectPath, CancellationToken ct) =>
      RunOperationAsync(projectPath, "Restoring…", c => RestoreCoreAsync(projectPath, c), ct);

  private async Task UpdatePackageCoreAsync(string projectPath, string packageId, CancellationToken ct)
  {
    var fullPath = Path.GetFullPath(projectPath);

    var installed = await buildHostManager.ListPackageReferencesAsync(fullPath, ct);
    var current = installed.FirstOrDefault(p => string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase));
    NuGetVersion? currentVersion = null;
    if (current is not null)
    {
      NuGetVersion.TryParse(current.Version, out currentVersion);
    }

    var includePrerelease = currentVersion?.IsPrerelease ?? false;

    var candidates = (await nugetService.GetPackageVersionsAsync(packageId, ct, includePrerelease: includePrerelease))
        .Where(v => currentVersion is null || v > currentVersion)
        .OrderByDescending(v => v)
        .ToList();

    if (candidates.Count == 0)
    {
      await editorService.DisplayMessage($"{packageId} is already up to date");
      return;
    }

    var choices = candidates
        .Select(v => new PickerChoice<string>(v.ToNormalizedString(), v.ToNormalizedString(), v.ToNormalizedString()))
        .ToArray();

    var label = currentVersion is null ? $"Pick a version for {packageId}" : $"Update {packageId} (current {currentVersion.ToNormalizedString()})";
    var version = await editorService.RequestPickerAsync(label, choices, ct: ct);
    if (version is null)
    {
      return;
    }

    await ApplyPackageVersionAsync(fullPath, packageId, version, ct);
  }

  private async Task ApplyPackageVersionAsync(string fullPath, string packageId, string version, CancellationToken ct)
  {
    var (success, _, err) = await processQueue.RunProcessAsync(
        "dotnet",
        $"add \"{fullPath}\" package \"{packageId}\" --version \"{version}\"",
        new ProcessOptions(KillOnTimeout: true),
        ct);

    if (!success)
    {
      logger.LogError("dotnet add package failed for {pkg}@{version}: {err}", packageId, version, err);
      await editorService.DisplayError($"Failed to update {packageId}: {err}");
      return;
    }

    if (await RestoreAndReportAsync(fullPath, ct))
    {
      await editorService.DisplayMessage($"Updated {packageId} to {version}");
    }
  }

  private async Task RemoveProjectReferenceCoreAsync(string projectPath, string targetPath, CancellationToken ct)
  {
    var (removed, error) = await projectReferenceCliService.RemoveProjectReferenceAsync(projectPath, targetPath, ct);
    var name = Path.GetFileNameWithoutExtension(targetPath);
    if (removed)
    {
      await editorService.DisplayMessage($"Reference to '{name}' removed");
    }
    else
    {
      await editorService.DisplayError(string.IsNullOrWhiteSpace(error)
          ? $"Failed to remove reference to '{name}'"
          : $"Failed to remove reference to '{name}': {error.Trim()}");
    }
  }

  private Task RestoreCoreAsync(string projectPath, CancellationToken ct) =>
      RestoreAndReportAsync(Path.GetFullPath(projectPath), ct);

  private async Task<bool> RestoreAndReportAsync(string fullPath, CancellationToken ct)
  {
    var results = await buildHostManager
        .RestoreNugetPackagesAsync(new RestoreRequest([fullPath]), ct)
        .ToListAsync(ct);

    var diagnostics = results
        .Where(r => r.Output is not null)
        .SelectMany(r => r.Output!.Diagnostics);

    return await editorService.ReportDiagnosticsAsync(diagnostics);
  }

  private async Task RunOperationAsync(string projectPath, string operation, Func<CancellationToken, Task> work, CancellationToken ct, bool refreshAfter = true)
  {
    await SendStatusAsync(projectPath, isLoading: true, operation);
    try
    {
      await work(ct);
    }
    finally
    {
      if (refreshAfter)
      {
        try
        {
          await NotifyUpdateAsync(projectPath, CancellationToken.None);
        }
        catch (Exception ex)
        {
          logger.LogWarning(ex, "Failed to refresh project view snapshot for {path}", projectPath);
        }
      }
      await SendStatusAsync(projectPath, isLoading: false, operation: null);
    }
  }

  private Task SendStatusAsync(string projectPath, bool isLoading, string? operation) =>
      rpc.NotifyWithParameterObjectAsync("projectview/status", new ProjectViewStatus(Path.GetFullPath(projectPath), isLoading, operation));

  private async Task NotifyUpdateAsync(string projectPath, CancellationToken ct)
  {
    var snapshot = await GetSnapshotAsync(projectPath, ct);
    await rpc.NotifyWithParameterObjectAsync("projectview/update", snapshot);
  }
}
