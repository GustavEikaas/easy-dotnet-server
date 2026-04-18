using System.Runtime.CompilerServices;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE;
using EasyDotnet.IDE.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.BuildHost;

public class WorkspaceBuildHostManager(
    ISolutionService solutionService,
    IBuildHostManager innerManager,
    ProjectEvaluationCache cache,
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

  public async IAsyncEnumerable<ProjectEvaluationResult> GetProjectPropertiesBatchAsync(
      GetProjectPropertiesBatchRequest request,
      [EnumeratorCancellation] CancellationToken ct)
  {
    var config = request.Configuration ?? "Debug";
    var missingPaths = new List<string>();
    var ownedTcs = new Dictionary<string, TaskCompletionSource<List<ProjectEvaluationResult>>>();
    var pendingTasks = new List<Task<List<ProjectEvaluationResult>>>();

    foreach (var path in request.ProjectPaths)
    {
      var (tcs, isNew) = cache.GetOrRegister(path, config);
      if (isNew)
      {
        missingPaths.Add(path);
        ownedTcs[path] = tcs;
      }
      else
      {
        pendingTasks.Add(tcs.Task);
      }
    }

    var fetchTask = missingPaths.Count > 0 ? ExecuteBatchFetchAsync(missingPaths, config, ct) : null;

    foreach (var task in pendingTasks)
    {
      foreach (var result in await task) { yield return result; }
    }

    if (fetchTask is not null)
    {
      await fetchTask;
      foreach (var path in missingPaths)
      {
        foreach (var result in await ownedTcs[path].Task) { yield return result; }
      }
    }
  }

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

  public async IAsyncEnumerable<RestoreResult> RestoreNugetPackagesAsync(
      RestoreRequest request,
      [EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var result in innerManager.RestoreNugetPackagesAsync(request, ct))
    {
      yield return result;
    }
    cache.Clear(CacheInvalidationReason.Restore);
  }

  /// <summary>
  /// Restores NuGet packages for all projects in a solution.
  /// </summary>
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

  public async IAsyncEnumerable<BatchBuildResult> BatchBuildAsync(
      BatchBuildRequest request,
      [EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var result in innerManager.BatchBuildAsync(request, ct))
    {
      yield return result;
    }
  }

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

  public void InvalidateCache(string projectPath, string config = "Debug") => cache.Invalidate(projectPath, config);

  public void ClearCache() => cache.Clear(CacheInvalidationReason.ClearedAll);

  public void Dispose() => innerManager.Dispose();
  public ValueTask DisposeAsync() => innerManager.DisposeAsync();

  private async Task ExecuteBatchFetchAsync(
      List<string> paths,
      string config,
      CancellationToken ct)
  {
    var buckets = paths.ToDictionary(p => p, _ => new List<ProjectEvaluationResult>());

    try
    {
      var request = new GetProjectPropertiesBatchRequest([.. paths], config);

      await foreach (var result in innerManager.GetProjectPropertiesBatchAsync(request, ct))
      {
        if (buckets.TryGetValue(result.ProjectPath, out var list)) { list.Add(result); }
      }

      foreach (var path in paths)
      {
        cache.Complete(path, config, buckets[path]);
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to evaluate MSBuild batch");
      foreach (var path in paths) { cache.Fault(path, config, ex); }
    }
  }
}