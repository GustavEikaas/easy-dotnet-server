using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Infrastructure;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.BuildHost;

public class WorkspaceBuildHostManager(IBuildHostManager innerManager, ILogger<WorkspaceBuildHostManager> logger) : IBuildHostManager
{
  private readonly ConcurrentDictionary<string, TaskCompletionSource<List<ProjectEvaluationResult>>> _evaluationCache = new();

  public async Task PreloadProjectsAsync(List<string> projectPaths, string configuration = "Debug", CancellationToken ct = default)
  {
    if (projectPaths.Count == 0) return;

    var request = new GetProjectPropertiesBatchRequest([.. projectPaths], configuration);
    _ = await GetProjectPropertiesBatchAsync(request, ct).ToListAsync(ct);
  }

  public async Task<IAsyncEnumerable<ProjectEvaluationResult>> GetProjectPropertiesBatchAsync(
      GetProjectPropertiesBatchRequest request,
      CancellationToken cancellationToken) =>
    EvaluateAndCacheAsync(request, cancellationToken);

  private async IAsyncEnumerable<ProjectEvaluationResult> EvaluateAndCacheAsync(
      GetProjectPropertiesBatchRequest request,
      [EnumeratorCancellation] CancellationToken ct)
  {
    var missingPaths = new List<string>();
    var myTcsDict = new Dictionary<string, TaskCompletionSource<List<ProjectEvaluationResult>>>();
    var tasksToAwait = new List<Task<List<ProjectEvaluationResult>>>();

    foreach (var path in request.ProjectPaths)
    {
      var tcs = new TaskCompletionSource<List<ProjectEvaluationResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
      var actualTcs = _evaluationCache.GetOrAdd(path, tcs);

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

    Task? fetchTask = null;
    if (missingPaths.Count > 0)
    {
      fetchTask = ExecuteBatchFetchAsync(missingPaths, request.Configuration, myTcsDict, ct);
    }

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

  private async Task ExecuteBatchFetchAsync(
      List<string> missingPaths,
      string? configuration,
      Dictionary<string, TaskCompletionSource<List<ProjectEvaluationResult>>> tcsDict,
      CancellationToken ct)
  {
    var resultsByProject = missingPaths.ToDictionary(p => p, _ => new List<ProjectEvaluationResult>());

    try
    {
      var request = new GetProjectPropertiesBatchRequest([.. missingPaths], configuration);
      var stream = await innerManager.GetProjectPropertiesBatchAsync(request, ct);

      await foreach (var result in stream.WithCancellation(ct))
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
          _evaluationCache.TryRemove(path, out _);
        }

        tcsDict[path].TrySetResult(list);
      }
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to evaluate MSBuild batch.");

      foreach (var path in missingPaths)
      {
        _evaluationCache.TryRemove(path, out _);
        tcsDict[path].TrySetException(ex);
      }
    }
  }

  public Task<GetWatchListResponse> GetProjectWatchListAsync(GetWatchListRequest request, CancellationToken ct)
      => innerManager.GetProjectWatchListAsync(request, ct);

  public Task<IAsyncEnumerable<RestoreResult>> RestoreNugetPackagesAsync(RestoreRequest request, CancellationToken ct)
      => innerManager.RestoreNugetPackagesAsync(request, ct);

  public void InvalidateCache(string projectPath) => _evaluationCache.TryRemove(projectPath, out _);
  public void ClearCache() => _evaluationCache.Clear();
  public void Dispose() => innerManager.Dispose();
  public ValueTask DisposeAsync() => innerManager.DisposeAsync();
}