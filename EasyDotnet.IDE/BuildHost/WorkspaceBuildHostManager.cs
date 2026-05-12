using System.Runtime.CompilerServices;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE;
using EasyDotnet.IDE.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.BuildHost;

public class WorkspaceBuildHostManager(
    ISolutionService solutionService,
    IBuildHostManager innerManager,
    ILogger<WorkspaceBuildHostManager> logger) : IBuildHostManager
{
  public async Task PreloadProjectsAsync(
      List<string> projectPaths,
      string configuration = "Debug",
      CancellationToken ct = default)
  {
    if (projectPaths.Count == 0) { return; }
    var request = new GetProjectPropertiesBatchRequest([.. projectPaths], configuration);
    await GetProjectPropertiesBatchAsync(request, ct).ToListAsync(ct);
  }

  public IAsyncEnumerable<ProjectEvaluationResult> GetProjectPropertiesBatchAsync(
      GetProjectPropertiesBatchRequest request,
      CancellationToken ct) =>
      innerManager.GetProjectPropertiesBatchAsync(request, ct);

  public async Task<ValidatedDotnetProject?> GetProjectAsync(
      string projectPath,
      string targetFramework,
      string configuration = "Debug",
      CancellationToken ct = default)
  {
    var request = new GetProjectPropertiesBatchRequest([projectPath], configuration);

    await foreach (var result in GetProjectPropertiesBatchAsync(request, ct))
    {
      if (result.Success && result.Project is not null && string.Equals(result.TargetFramework, targetFramework, StringComparison.OrdinalIgnoreCase))
      {
        return result.Project;
      }
    }

    logger.LogWarning("Could not resolve {ProjectPath} for TFM {TFM}", projectPath, targetFramework);
    return null;
  }

  public async Task<List<ValidatedDotnetProject>> GetProjectsFromSolutionAsync(
      string solutionPath,
      Func<ValidatedDotnetProject, bool>? filter = null,
      string configuration = "Debug",
      CancellationToken ct = default)
  {
    var solutionProjects = await solutionService.GetProjectsFromSolutionFile(solutionPath, ct);
    var projectPaths = solutionProjects.ConvertAll(x => x.AbsolutePath);

    if (projectPaths.Count == 0) { return []; }

    var results = new List<ValidatedDotnetProject>();
    var request = new GetProjectPropertiesBatchRequest([.. projectPaths], configuration);

    await foreach (var result in GetProjectPropertiesBatchAsync(request, ct))
    {
      if (result.Success && result.Project is not null)
      {
        if (filter is null || filter(result.Project)) { results.Add(result.Project); }
      }
      else if (!result.Success)
      {
        logger.LogWarning(
            "Failed to evaluate {ProjectPath} ({TFM}): {Error}",
            result.ProjectPath,
            result.TargetFramework ?? "unknown",
            result.Error?.Message);
      }
    }

    return results;
  }

  public Task<List<ValidatedDotnetProject>> GetTestProjectsFromSolutionAsync(
      string solutionPath,
      string configuration = "Debug",
      CancellationToken ct = default) =>
      GetProjectsFromSolutionAsync(
          solutionPath,
          p => p.IsMTP || p.IsVsTest,
          configuration,
          ct);

  public IAsyncEnumerable<RestoreResult> RestoreNugetPackagesAsync(
      RestoreRequest request,
      CancellationToken ct) =>
      innerManager.RestoreNugetPackagesAsync(request, ct);

  public async IAsyncEnumerable<RestoreResult> RestoreNugetPackagesSolutionAsync(
      string solutionPath,
      [EnumeratorCancellation] CancellationToken ct = default)
  {
    var solutionProjects = await solutionService.GetProjectsFromSolutionFile(solutionPath, ct);
    var projectPaths = solutionProjects.ConvertAll(x => x.AbsolutePath);

    if (projectPaths.Count == 0) yield break;

    await foreach (var result in RestoreNugetPackagesAsync(new RestoreRequest([.. projectPaths]), ct))
    {
      yield return result;
    }
  }

  public IAsyncEnumerable<BatchBuildResult> BatchBuildAsync(
      BatchBuildRequest request,
      CancellationToken ct) =>
      innerManager.BatchBuildAsync(request, ct);

  public Task<GetWatchListResponse> GetProjectWatchListAsync(
      GetWatchListRequest request,
      CancellationToken ct) =>
      innerManager.GetProjectWatchListAsync(request, ct);

  public Task<ConvertSingleFileResponse> ConvertFileToProjectAsync(string entryPointFilePath, CancellationToken cancellationToken) =>
      innerManager.ConvertFileToProjectAsync(entryPointFilePath, cancellationToken);

  public Task<BuildServerDiagnosticsResponse> GetBuildServerDiagnosticsAsync(CancellationToken cancellationToken) =>
      innerManager.GetBuildServerDiagnosticsAsync(cancellationToken);

  public Task<InstalledPackageReference[]> ListPackageReferencesAsync(string projectPath, CancellationToken cancellationToken) =>
      innerManager.ListPackageReferencesAsync(projectPath, cancellationToken);

  public Task SetLogLevelAsync(string level, CancellationToken cancellationToken) =>
      innerManager.SetLogLevelAsync(level, cancellationToken);

  public Task<string[]> GetLogsAsync(CancellationToken cancellationToken) =>
      innerManager.GetLogsAsync(cancellationToken);

  public void Dispose() => innerManager.Dispose();
  public ValueTask DisposeAsync() => innerManager.DisposeAsync();
}
