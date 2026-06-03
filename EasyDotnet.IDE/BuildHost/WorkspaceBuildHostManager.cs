using System.Runtime.CompilerServices;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Solution;
using EasyDotnet.IDE.Workspace.BuildConfiguration;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.BuildHost;

public class WorkspaceBuildHostManager : IBuildHostManager
{
  private readonly ISolutionService _solutionService;
  private readonly IBuildHostManager _innerManager;
  private readonly ProjectEvaluationCache _cache;
  private readonly IWorkspaceBuildConfigurationService _workspaceBuildConfigurationService;
  private readonly ILogger<WorkspaceBuildHostManager> _logger;

  public WorkspaceBuildHostManager(
      ISolutionService solutionService,
      IBuildHostManager innerManager,
      ProjectEvaluationCache cache,
      IWorkspaceBuildConfigurationService workspaceBuildConfigurationService,
      ILogger<WorkspaceBuildHostManager> logger)
  {
    _solutionService = solutionService;
    _innerManager = innerManager;
    _cache = cache;
    _workspaceBuildConfigurationService = workspaceBuildConfigurationService;
    _logger = logger;

    workspaceBuildConfigurationService.ConfigurationChanged += _ => cache.Clear(CacheInvalidationReason.ClearedAll);
  }

  public async Task PreloadProjectsAsync(
      List<string> projectPaths,
      string? configuration = null,
      string? platform = null,
      CancellationToken ct = default)
  {
    if (projectPaths.Count == 0) { return; }
    var request = new GetProjectPropertiesBatchRequest([.. projectPaths], configuration, platform);
    await GetProjectPropertiesBatchAsync(request, ct).ToListAsync(ct);
  }

  public async IAsyncEnumerable<ProjectEvaluationResult> GetProjectPropertiesBatchAsync(
      GetProjectPropertiesBatchRequest request,
      [EnumeratorCancellation] CancellationToken ct)
  {
    var resolvedTargets = await ResolveTargetsAsync(request.ProjectPaths, request.Configuration, request.Platform, ct);
    var fetchGroups = new Dictionary<(string Configuration, string Platform, bool ComputeRunArguments), List<string>>();
    var tasksByTarget = new Dictionary<(string Path, string Configuration, string Platform, bool ComputeRunArguments), Task<List<ProjectEvaluationResult>>>();

    foreach (var target in resolvedTargets)
    {
      var cacheKey = (target.TargetPath, target.Configuration, target.Platform ?? "", request.ComputeRunArguments);
      var (tcs, isNew) = _cache.GetOrRegister(target.TargetPath, target.Configuration, target.Platform, request.ComputeRunArguments);
      tasksByTarget[cacheKey] = tcs.Task;

      if (isNew)
      {
        var groupKey = (target.Configuration, target.Platform ?? "", request.ComputeRunArguments);
        if (!fetchGroups.TryGetValue(groupKey, out var paths))
        {
          paths = [];
          fetchGroups[groupKey] = paths;
        }
        paths.Add(target.TargetPath);
      }
    }

    var fetchTasks = fetchGroups
        .Select(group => ExecuteBatchFetchAsync(group.Value, group.Key.Configuration, group.Key.Platform, group.Key.ComputeRunArguments, ct))
        .ToList();

    foreach (var target in resolvedTargets)
    {
      foreach (var result in await tasksByTarget[(target.TargetPath, target.Configuration, target.Platform ?? "", request.ComputeRunArguments)])
      {
        yield return result;
      }
    }

    await Task.WhenAll(fetchTasks);
  }

  public async Task<ValidatedDotnetProject?> GetProjectAsync(
      string projectPath,
      string targetFramework,
      string? configuration = null,
      string? platform = null,
      bool computeRunArguments = false,
      CancellationToken ct = default)
  {
    var request = new GetProjectPropertiesBatchRequest([projectPath], configuration, platform, computeRunArguments);

    await foreach (var result in GetProjectPropertiesBatchAsync(request, ct))
    {
      if (result.Success && result.Project is not null && string.Equals(result.TargetFramework, targetFramework, StringComparison.OrdinalIgnoreCase))
      {
        return result.Project;
      }
    }

    _logger.LogWarning("Could not resolve {ProjectPath} for TFM {TFM}", projectPath, targetFramework);
    return null;
  }

  public async Task<List<ValidatedDotnetProject>> GetProjectsFromDirectoryAsync(
      string rootDir,
      Func<ValidatedDotnetProject, bool>? filter = null,
      int maxDepth = 3,
      string? configuration = null,
      string? platform = null,
      CancellationToken ct = default)
  {
    var csprojFiles = Directory.EnumerateFiles(rootDir, "*.csproj", new EnumerationOptions
    {
      MaxRecursionDepth = maxDepth,
      RecurseSubdirectories = true
    });

    var results = new List<ValidatedDotnetProject>();
    var request = new GetProjectPropertiesBatchRequest([.. csprojFiles], configuration, platform);

    await foreach (var result in GetProjectPropertiesBatchAsync(request, ct))
    {
      if (result.Success && result.Project is not null)
      {
        if (filter is null || filter(result.Project)) { results.Add(result.Project); }
      }
      else if (!result.Success)
      {
        _logger.LogWarning(
            "Failed to evaluate {ProjectPath} ({TFM}): {Error}",
            result.ProjectPath,
            result.TargetFramework ?? "unknown",
            result.Error?.Message);
      }
    }

    return [.. results.DistinctBy(p => p.ProjectFullPath)];
  }

  public async Task<List<ValidatedDotnetProject>> GetProjectsFromSolutionAsync(
      string solutionPath,
      Func<ValidatedDotnetProject, bool>? filter = null,
      string? configuration = null,
      string? platform = null,
      CancellationToken ct = default)
  {
    var solutionProjects = await _solutionService.GetProjectsFromSolutionFile(solutionPath, ct);
    var projectPaths = solutionProjects.ConvertAll(x => x.AbsolutePath);

    if (projectPaths.Count == 0) { return []; }

    var results = new List<ValidatedDotnetProject>();
    var request = new GetProjectPropertiesBatchRequest([.. projectPaths], configuration, platform);

    await foreach (var result in GetProjectPropertiesBatchAsync(request, ct))
    {
      if (result.Success && result.Project is not null)
      {
        if (filter is null || filter(result.Project)) { results.Add(result.Project); }
      }
      else if (!result.Success)
      {
        _logger.LogWarning(
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
      string? configuration = null,
      string? platform = null,
      CancellationToken ct = default) =>
      GetProjectsFromSolutionAsync(
          solutionPath,
          p => p.IsMTP || p.IsVsTest,
          configuration,
          platform,
          ct);

  public async IAsyncEnumerable<RestoreResult> RestoreNugetPackagesAsync(
      RestoreRequest request,
      [EnumeratorCancellation] CancellationToken ct)
  {
    var resolvedTargets = await ResolveDirectTargetsAsync(request.ProjectPaths, request.Configuration, request.Platform, ct);
    var groups = resolvedTargets
        .GroupBy(target => (target.Configuration, target.Platform), target => target.TargetPath)
        .ToList();

    foreach (var group in groups)
    {
      var restoreRequest = request with
      {
        ProjectPaths = [.. group],
        Configuration = group.Key.Configuration,
        Platform = group.Key.Platform
      };

      await foreach (var result in _innerManager.RestoreNugetPackagesAsync(restoreRequest, ct))
      {
        yield return result;
      }
    }

    _cache.Clear(CacheInvalidationReason.Restore);
  }

  public async IAsyncEnumerable<RestoreResult> RestoreNugetPackagesSolutionAsync(
      string solutionPath,
      [EnumeratorCancellation] CancellationToken ct = default)
  {
    var solutionProjects = await _solutionService.GetProjectsFromSolutionFile(solutionPath, ct);
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
    var resolvedTargets = await ResolveDirectTargetsAsync(request.ProjectPaths, request.Configuration, request.Platform, ct);
    var groups = resolvedTargets
        .Where(target => target.Build)
        .GroupBy(target => (target.Configuration, target.Platform), target => target.TargetPath)
        .ToList();

    foreach (var group in groups)
    {
      var batchRequest = request with
      {
        ProjectPaths = [.. group],
        Configuration = group.Key.Configuration,
        Platform = group.Key.Platform
      };

      await foreach (var result in _innerManager.BatchBuildAsync(batchRequest, ct))
      {
        yield return result;
      }
    }
  }

  public Task<GetWatchListResponse> GetProjectWatchListAsync(
      GetWatchListRequest request,
      CancellationToken ct)
  {
    if (!string.IsNullOrWhiteSpace(request.Configuration) || !string.IsNullOrWhiteSpace(request.Platform))
    {
      return _innerManager.GetProjectWatchListAsync(request, ct);
    }

    return ResolveWatchListAsync(request.ProjectPath, ct);
  }

  public Task<ConvertSingleFileResponse> ConvertFileToProjectAsync(string entryPointFilePath, CancellationToken cancellationToken) =>
      _innerManager.ConvertFileToProjectAsync(entryPointFilePath, cancellationToken);

  public Task<BuildServerDiagnosticsResponse> GetBuildServerDiagnosticsAsync(CancellationToken cancellationToken) =>
      _innerManager.GetBuildServerDiagnosticsAsync(cancellationToken);

  public Task<InstalledPackageReference[]> ListPackageReferencesAsync(string projectPath, CancellationToken cancellationToken) =>
      _innerManager.ListPackageReferencesAsync(projectPath, cancellationToken);

  public Task SetLogLevelAsync(string level, CancellationToken cancellationToken) =>
      _innerManager.SetLogLevelAsync(level, cancellationToken);

  public Task<string[]> GetLogsAsync(CancellationToken cancellationToken) =>
      _innerManager.GetLogsAsync(cancellationToken);

  public void InvalidateCache(string projectPath, string? config = null, string? platform = null) => _cache.Invalidate(projectPath, config, platform);

  public void ClearCache() => _cache.Clear(CacheInvalidationReason.ClearedAll);

  public void Dispose() => _innerManager.Dispose();
  public ValueTask DisposeAsync() => _innerManager.DisposeAsync();

  private async Task ExecuteBatchFetchAsync(
      List<string> paths,
      string config,
      string? platform,
      bool computeRunArguments,
      CancellationToken ct)
  {
    var buckets = paths.ToDictionary(p => p, _ => new List<ProjectEvaluationResult>());

    try
    {
      var request = new GetProjectPropertiesBatchRequest([.. paths], config, platform, computeRunArguments);

      await foreach (var result in _innerManager.GetProjectPropertiesBatchAsync(request, ct))
      {
        if (buckets.TryGetValue(result.ProjectPath, out var list)) { list.Add(result); }
      }

      foreach (var path in paths)
      {
        _cache.Complete(path, config, platform, computeRunArguments, buckets[path]);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to evaluate MSBuild batch");
      foreach (var path in paths) { _cache.Fault(path, config, platform, computeRunArguments, ex); }
    }
  }

  private async Task<IReadOnlyList<ResolvedBuildConfiguration>> ResolveTargetsAsync(
      IReadOnlyCollection<string> targetPaths,
      string? configuration,
      string? platform,
      CancellationToken ct)
  {
    var expandedTargetPaths = await ExpandSolutionTargetsAsync(targetPaths, ct);

    if (!string.IsNullOrWhiteSpace(configuration) || !string.IsNullOrWhiteSpace(platform))
    {
      var explicitConfiguration = configuration ?? "Debug";
      var workspaceConfiguration = new WorkspaceBuildConfiguration(explicitConfiguration, platform);
      return [.. expandedTargetPaths.Select(path => new ResolvedBuildConfiguration(
          path,
          workspaceConfiguration,
          explicitConfiguration,
          ResolveExplicitPlatform(path, platform),
          Build: true,
          Deploy: false,
          UsedProjectMapping: false))];
    }

    return await _workspaceBuildConfigurationService.ResolveTargetsAsync(expandedTargetPaths, ct);
  }

  private Task<IReadOnlyList<ResolvedBuildConfiguration>> ResolveDirectTargetsAsync(
      IReadOnlyCollection<string> targetPaths,
      string? configuration,
      string? platform,
      CancellationToken ct)
  {
    if (!string.IsNullOrWhiteSpace(configuration) || !string.IsNullOrWhiteSpace(platform))
    {
      var explicitConfiguration = configuration ?? "Debug";
      var workspaceConfiguration = new WorkspaceBuildConfiguration(explicitConfiguration, platform);
      return Task.FromResult<IReadOnlyList<ResolvedBuildConfiguration>>(
          [.. targetPaths.Select(path => new ResolvedBuildConfiguration(
              path,
              workspaceConfiguration,
              explicitConfiguration,
              ResolveExplicitPlatform(path, platform),
              Build: true,
              Deploy: false,
              UsedProjectMapping: false))]);
    }

    return _workspaceBuildConfigurationService.ResolveTargetsAsync(targetPaths, ct);
  }

  private async Task<GetWatchListResponse> ResolveWatchListAsync(string projectPath, CancellationToken ct)
  {
    var resolved = await _workspaceBuildConfigurationService.ResolveTargetAsync(projectPath, ct);
    return await _innerManager.GetProjectWatchListAsync(
        new GetWatchListRequest(projectPath, resolved.Configuration, resolved.Platform),
        ct);
  }

  private static string? ResolveExplicitPlatform(string targetPath, string? platform) =>
      DotnetFileTypes.IsAnySolutionFile(targetPath)
          ? platform
          : MsBuildPlatform.ToProjectPlatform(platform);

  private async Task<IReadOnlyList<string>> ExpandSolutionTargetsAsync(
      IReadOnlyCollection<string> targetPaths,
      CancellationToken ct)
  {
    var expanded = new List<string>();

    foreach (var targetPath in targetPaths)
    {
      if (!DotnetFileTypes.IsAnySolutionFile(targetPath))
      {
        expanded.Add(targetPath);
        continue;
      }

      var solutionProjects = await _solutionService.GetProjectsFromSolutionFile(targetPath, ct);
      expanded.AddRange(solutionProjects.OnlyDotnetProjects().Select(project => project.AbsolutePath));
    }

    return expanded;
  }
}