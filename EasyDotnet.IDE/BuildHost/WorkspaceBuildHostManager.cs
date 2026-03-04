using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Infrastructure;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.BuildHost;

public class WorkspaceBuildHostManager(ISolutionService solutionService, IBuildHostManager innerManager, ILogger<WorkspaceBuildHostManager> logger) : IBuildHostManager
{
  private readonly ConcurrentDictionary<(string Path, string Config), TaskCompletionSource<List<ProjectEvaluationResult>>> _evaluationCache = new();

  public async Task PreloadProjectsAsync(List<string> projectPaths, string configuration = "Debug", CancellationToken ct = default)
  {
    if (projectPaths.Count == 0) return;
    var request = new GetProjectPropertiesBatchRequest([.. projectPaths], configuration);
    await GetProjectPropertiesBatchAsync(request, ct).ToListAsync(ct);
  }

  public async IAsyncEnumerable<ProjectEvaluationResult> GetProjectPropertiesBatchAsync(
      GetProjectPropertiesBatchRequest request,
      [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    var missingPaths = new List<string>();
    var myTcsDict = new Dictionary<string, TaskCompletionSource<List<ProjectEvaluationResult>>>();
    var tasksToAwait = new List<Task<List<ProjectEvaluationResult>>>();
    var config = request.Configuration ?? "Debug";

    foreach (var path in request.ProjectPaths)
    {
      var key = (path, config);
      var tcs = new TaskCompletionSource<List<ProjectEvaluationResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
      var actualTcs = _evaluationCache.GetOrAdd(key, tcs);

      if (ReferenceEquals(actualTcs, tcs))
      {
        missingPaths.Add(path);
        myTcsDict[path] = tcs;
      }
      else
      {
        tasksToAwait.Add(actualTcs.Task);
      }
    }

    var fetchTask = missingPaths.Count > 0
        ? ExecuteBatchFetchAsync(missingPaths, config, myTcsDict, cancellationToken)
        : null;

    foreach (var task in tasksToAwait)
    {
      var results = await task;
      foreach (var result in results) yield return result;
    }

    if (fetchTask != null)
    {
      await fetchTask;
      foreach (var path in missingPaths)
      {
        var results = await myTcsDict[path].Task;
        foreach (var result in results) yield return result;
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
      if (result.Success
          && result.Project is not null
          && string.Equals(result.TargetFramework, targetFramework, StringComparison.OrdinalIgnoreCase))
      {
        return result.Project;
      }
    }

    logger.LogWarning(
        "Could not resolve {ProjectPath} for TFM {TFM}",
        projectPath, targetFramework);

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

    if (projectPaths.Count == 0) return [];

    var results = new List<ValidatedDotnetProject>();
    var request = new GetProjectPropertiesBatchRequest([.. projectPaths], configuration);

    await foreach (var result in GetProjectPropertiesBatchAsync(request, ct))
    {
      if (result.Success && result.Project is not null)
      {
        if (filter is null || filter(result.Project))
          results.Add(result.Project);
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

  /// <summary>
  /// Returns all test projects from a solution, flattened across TFMs.
  /// A project qualifies if IsTestProject or IsTestPlatformProject is true.
  /// </summary>
  public Task<List<ValidatedDotnetProject>> GetTestProjectsFromSolutionAsync(
      string solutionPath,
      string configuration = "Debug",
      CancellationToken ct = default) =>
      GetProjectsFromSolutionAsync(
          solutionPath,
          p => p.IsMTP || p.IsVsTest,
          configuration,
          ct);

  private async Task ExecuteBatchFetchAsync(
      List<string> missingPaths,
      string config,
      Dictionary<string, TaskCompletionSource<List<ProjectEvaluationResult>>> tcsDict,
      CancellationToken ct)
  {
    var resultsByProject = missingPaths.ToDictionary(p => p, _ => new List<ProjectEvaluationResult>());

    try
    {
      var request = new GetProjectPropertiesBatchRequest([.. missingPaths], config);

      await foreach (var result in innerManager.GetProjectPropertiesBatchAsync(request, ct))
      {
        if (resultsByProject.TryGetValue(result.ProjectPath, out var list))
        {
          list.Add(result);
        }
      }

      foreach (var path in missingPaths)
      {
        var list = resultsByProject[path];
        if (list.Count == 0 || list.Any(r => !r.Success))
        {
          _evaluationCache.TryRemove((path, config), out _);
        }

        tcsDict[path].TrySetResult(list);
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to evaluate MSBuild batch.");
      foreach (var path in missingPaths)
      {
        _evaluationCache.TryRemove((path, config), out _);
        tcsDict[path].TrySetException(ex);
      }
    }
  }

  public async IAsyncEnumerable<RestoreResult> RestoreNugetPackagesAsync(
      RestoreRequest request,
      [EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var result in innerManager.RestoreNugetPackagesAsync(request, ct))
    {
      yield return result;
    }
  }

  public Task<GetWatchListResponse> GetProjectWatchListAsync(GetWatchListRequest request, CancellationToken ct)
      => innerManager.GetProjectWatchListAsync(request, ct);

  public void InvalidateCache(string projectPath, string config = "Debug") =>
      _evaluationCache.TryRemove((projectPath, config), out _);

  public void ClearCache() => _evaluationCache.Clear();
  public void Dispose() => innerManager.Dispose();
  public ValueTask DisposeAsync() => innerManager.DisposeAsync();
}