using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.TestRunner.Adapters;
using EasyDotnet.IDE.TestRunner.Analysis;
using EasyDotnet.IDE.TestRunner.Dispatch;
using EasyDotnet.IDE.TestRunner.Executor;
using EasyDotnet.IDE.TestRunner.Lock;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;
using EasyDotnet.IDE.TestRunner.Store;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.TestRunner.Service;

public class TestRunnerService(
    NodeRegistry registry,
    StatusDispatcher dispatcher,
    DetailStore detailStore,
    BuildErrorStore buildErrorStore,
    GlobalOperationLock operationLock,
    OperationExecutor executor,
    AdapterResolver adapterResolver,
    WorkspaceBuildHostManager buildHost,
    ILogger<TestRunnerService> logger)
{
  public async Task QuickDiscoverAsync(string solutionPath, CancellationToken ct)
  {
    using var token = operationLock.TryAcquire("quickDiscover", ct);
    if (token is null) return;

    registry.Clear();
    detailStore.ClearAll();
    await adapterResolver.InvalidateAllAsync();

    var solutionName = Path.GetFileName(solutionPath);
    var solutionId = NodeIdBuilder.Solution(solutionName);
    var solutionNode = new TestNode(
        Id: solutionId, DisplayName: solutionName,
        ParentId: null, FilePath: solutionPath,
        SignatureLine: null, BodyStartLine: null, EndLine: null,
        Type: new NodeType.Solution(), ProjectId: null,
        AvailableActions: [TestAction.Run, TestAction.Invalidate]);
    registry.Register(solutionNode);
    await dispatcher.SendRegisterTestAsync(solutionNode);

    var testProjects = await buildHost.GetTestProjectsFromSolutionAsync(solutionPath, ct: token.Ct);

    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: true, CurrentOperation: "Discovering",
        OverallStatus: OverallStatus.Discovering,
        TotalTests: 0, TotalRunning: 0,
        TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

    var discoverTasks = testProjects.Select(project =>
        executor.DiscoverProjectAsync(project, solutionId, token));

    await Task.WhenAll(discoverTasks);

    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: false, CurrentOperation: null,
        OverallStatus: OverallStatus.Idle,
        TotalTests: registry.GetLeafCount(), TotalRunning: 0,
        TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));
  }

  public async Task<InitializeResult> InitializeAsync(string solutionPath, CancellationToken ct)
  {
    using var token = await operationLock.WaitAcquireAsync("initialize", ct);

    var solutionName = Path.GetFileName(solutionPath);
    var solutionId = NodeIdBuilder.Solution(solutionName);

    if (!registry.Exists(solutionId))
    {
      var solutionNode = new TestNode(
          Id: solutionId,
          DisplayName: solutionName,
          ParentId: null,
          FilePath: solutionPath,
          SignatureLine: null, BodyStartLine: null, EndLine: null,
          Type: new NodeType.Solution(),
          ProjectId: null,
          AvailableActions: [TestAction.Run, TestAction.Invalidate]);
      registry.Register(solutionNode);
      await dispatcher.SendRegisterTestAsync(solutionNode);
    }

    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: true, CurrentOperation: "Restoring",
        OverallStatus: OverallStatus.Building,
        TotalTests: registry.GetLeafCount(), TotalRunning: 0,
        TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

    try
    {
      await foreach (var result in buildHost.RestoreNugetPackagesAsync(
          new RestoreRequest([solutionPath]), token.Ct))
      {
        if (!result.Success)
          logger.LogWarning("Restore failed for {Path}: {Error}", result.ProjectPath, result.ErrorMessage);
      }
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Optimistic restore failed — continuing anyway");
    }

    var testProjects = await buildHost.GetTestProjectsFromSolutionAsync(solutionPath, ct: token.Ct);

    var projectsByPath = testProjects
        .GroupBy(p => p.ProjectFullPath, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    var needsBuild = projectsByPath.Values
        .SelectMany(variants => variants)
        .Where(project =>
        {
          var projectId = NodeIdBuilder.Project(solutionId, project.ProjectName, project.TargetFramework ?? "");
          return !registry.HasDescendants(projectId);
        })
        .GroupBy(p => p.ProjectFullPath, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    if (needsBuild.Count == 0)
    {
      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: false, CurrentOperation: null,
          OverallStatus: OverallStatus.Idle,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));
      return new InitializeResult(Success: true);
    }

    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: true, CurrentOperation: "Building",
        OverallStatus: OverallStatus.Building,
        TotalTests: registry.GetLeafCount(), TotalRunning: 0,
        TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

    var buildRequest = new BatchBuildRequest(
        ProjectPaths: [.. needsBuild.Keys],
        Configuration: null);


    var discoverTasks = new List<Task>();

    try
    {
      await foreach (var result in buildHost.BatchBuildAsync(buildRequest, token.Ct))
      {
        token.Ct.ThrowIfCancellationRequested();

        var tfmVariants = needsBuild.GetValueOrDefault(result.ProjectPath);
        if (tfmVariants is null) continue;

        if (result.Kind == BatchBuildResultKind.Started)
        {
          foreach (var project in tfmVariants)
          {
            var projectId = NodeIdBuilder.Project(solutionId, project.ProjectName, project.TargetFramework ?? "");
            await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.Building());
          }
          continue;
        }

        foreach (var project in tfmVariants)
        {
          var projectId = NodeIdBuilder.Project(solutionId, project.ProjectName, project.TargetFramework ?? "");

          if (result.Success != true)
          {
            await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.BuildFailed(), [TestAction.GetBuildErrors]);
            if (result.Output?.Diagnostics is { } initDiags && initDiags.Length > 0)
            {
              buildErrorStore.Set(projectId, initDiags);
            }

            continue;
          }
          buildErrorStore.Clear(projectId);
          discoverTasks.Add(executor.DiscoverProjectAsync(project, solutionId, token));
        }
      }

      token.Ct.ThrowIfCancellationRequested();

      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: true, CurrentOperation: "Discovering",
          OverallStatus: OverallStatus.Discovering,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

      await Task.WhenAll(discoverTasks);

      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: false, CurrentOperation: null,
          OverallStatus: OverallStatus.Idle,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

      return new InitializeResult(Success: true);
    }
    catch (OperationCanceledException)
    {
      await ResetTransientNodesAsync();
      await dispatcher.SendRunnerStatusAsync(BuildIdleStatus());
      return new InitializeResult(Success: false);
    }
  }

  public async Task<OperationResult> RunAsync(string nodeId, CancellationToken ct)
      => await ExecuteOnNodeAsync(nodeId, "run", debug: false, ct);

  public async Task<OperationResult> DebugAsync(string nodeId, CancellationToken ct)
      => await ExecuteOnNodeAsync(nodeId, "debug", debug: true, ct);

  public async Task<OperationResult> InvalidateAsync(string nodeId, CancellationToken ct)
  {
    using var token = operationLock.TryAcquire("invalidate", ct)
        ?? throw new InvalidOperationException("Operation already in progress");

    await dispatcher.SendRunnerStatusAsync(BuildLoadingStatus("Invalidating"));
    await dispatcher.SendBatchStatusAsync(nodeId, null, registry);

    var node = registry.Get(nodeId)
        ?? throw new KeyNotFoundException($"Node {nodeId} not found");

    var projectIds = node.Type is NodeType.Project
        ? [nodeId]
        : registry.GetDescendants(nodeId)
            .Where(n => n.Type is NodeType.Project)
            .Select(n => n.Id)
            .ToList();

    try
    {
      var projectsByPath = new Dictionary<string, List<ValidatedDotnetProject>>(
          StringComparer.OrdinalIgnoreCase);

      foreach (var projectId in projectIds)
      {
        var project = await ResolveProjectAsync(projectId, token.Ct);
        if (project is null) continue;

        await adapterResolver.InvalidateAsync(project.ProjectFullPath);
        buildHost.InvalidateCache(project.ProjectFullPath);

        if (!projectsByPath.TryGetValue(project.ProjectFullPath, out var variants))
          projectsByPath[project.ProjectFullPath] = variants = [];
        variants.Add(project);
      }

      if (projectsByPath.Count == 0)
      {
        await dispatcher.SendRunnerStatusAsync(BuildIdleStatus());
        return new OperationResult(Success: true);
      }

      var buildRequest = new BatchBuildRequest(
          ProjectPaths: [.. projectsByPath.Keys],
          Configuration: null);

      var discoverTasks = new List<Task>();

      await foreach (var result in buildHost.BatchBuildAsync(buildRequest, token.Ct))
      {
        token.Ct.ThrowIfCancellationRequested();

        var variants = projectsByPath.GetValueOrDefault(result.ProjectPath);
        if (variants is null) continue;

        if (result.Kind == BatchBuildResultKind.Started)
        {
          foreach (var project in variants)
          {
            var pid = NodeIdBuilder.Project(node.ParentId ?? "", project.ProjectName, project.TargetFramework ?? "");
            await dispatcher.SendStatusAsync(pid, new TestNodeStatus.Building());
          }
          continue;
        }

        foreach (var project in variants)
        {
          var pid = NodeIdBuilder.Project(node.ParentId ?? "", project.ProjectName, project.TargetFramework ?? "");
          if (result.Success != true)
          {
            await dispatcher.SendStatusAsync(pid, new TestNodeStatus.BuildFailed(), [TestAction.GetBuildErrors]);
            if (result.Output?.Diagnostics is { } invDiags && invDiags.Length > 0)
            {
              buildErrorStore.Set(pid, invDiags);
            }
            continue;
          }
          buildErrorStore.Clear(pid);
          discoverTasks.Add(executor.DiscoverProjectAsync(project, node.ParentId ?? "", token));
        }
      }

      token.Ct.ThrowIfCancellationRequested();

      token.Ct.ThrowIfCancellationRequested();

      await Task.WhenAll(discoverTasks);
      await dispatcher.SendRunnerStatusAsync(BuildIdleStatus());
      return new OperationResult(Success: true);
    }
    catch (OperationCanceledException)
    {
      await ResetTransientNodesAsync();
      await dispatcher.SendRunnerStatusAsync(BuildIdleStatus());
      return new OperationResult(Success: false);
    }
  }

  public async Task GetBuildErrorsAsync(string nodeId)
  {
    var node = registry.Get(nodeId);
    var projectId = node?.Type is NodeType.Project ? nodeId : node?.ProjectId;
    if (projectId is null) return;

    var errors = buildErrorStore.Get(projectId);
    if (errors is null or { Length: 0 }) return;

    await dispatcher.SendQuickFixAsync(errors.Where(x => x.Severity == BuildDiagnosticSeverity.Error));
  }

  public GetResultsResult GetResults(string nodeId)
  {
    var detail = detailStore.Get(nodeId);
    if (detail is null)
    {
      return new GetResultsResult(Found: false, null, null, null, null, null);
    }

    return new GetResultsResult(
        Found: true,
        ErrorMessage: detail.ErrorMessage,
        Stdout: detail.Stdout,
        Frames: [.. detail.Frames.Select(f => new StackFrameDto(
            OriginalText: f.OriginalText,
            File: f.File,
            Line: f.Line,
            IsUserCode: f.IsUserCode))],
        FailingFrame: detail.FailingFrame is { } ff
            ? new StackFrameDto(ff.OriginalText, ff.File, ff.Line, ff.IsUserCode)
            : null,
        DurationDisplay: detail.DurationMs.HasValue
            ? FormatDuration(detail.DurationMs.Value)
            : null
    );
  }

  public SyncFileResult SyncFile(SyncFileRequest req)
  {
    var parsed = TestSourceLocator.ParseContent(req.Content);
    var updates = new List<LineNumberUpdateDto>();

    foreach (var node in registry.GetNodesForFile(req.Path))
    {
      var loc = node.Type is NodeType.TestClass
          ? (parsed.Classes.TryGetValue(node.DisplayName, out var clsLoc) ? clsLoc : null)
          : TestSourceLocator.LookupMethod(parsed.Methods, node.DisplayName);
      if (loc is null) continue;

      var changed = registry.UpdateLineNumbers(
          node.Id, loc.SignatureLine, loc.BodyStartLine, loc.EndLine);

      if (changed)
      {
        updates.Add(new LineNumberUpdateDto(node.Id, loc.SignatureLine, loc.BodyStartLine, loc.EndLine));
      }
    }

    return new SyncFileResult([.. updates], req.Version);
  }

  private async Task<OperationResult> ExecuteOnNodeAsync(
      string nodeId, string opName, bool debug, CancellationToken ct)
  {
    using var token = operationLock.TryAcquire(opName, ct)
        ?? throw new InvalidOperationException("Operation already in progress");

    var node = registry.Get(nodeId)
        ?? throw new KeyNotFoundException($"Node {nodeId} not found");

    return node.Type is NodeType.Solution
        ? await ExecuteMultiProjectAsync(nodeId, node, token, debug)
        : await ExecuteSingleProjectAsync(nodeId, node, token, debug);
  }

  private async Task<OperationResult> ExecuteMultiProjectAsync(
      string nodeId, TestNode node, OperationToken token, bool debug)
  {
    try
    {
      var projectNodes = (node.Type is NodeType.Project
              ? [node]
              : registry.GetDescendants(nodeId).Where(n => n.Type is NodeType.Project))
          .ToList();

      if (projectNodes.Count == 0)
      {
        await dispatcher.SendRunnerStatusAsync(BuildIdleStatus());
        return new OperationResult(Success: true);
      }

      var projects = (await Task.WhenAll(
              projectNodes.Select(pn => ResolveProjectAsync(pn.Id, token.Ct))))
          .Where(p => p is not null)
          .Select(p => p!)
          .ToList();

      await dispatcher.SendRunnerStatusAsync(BuildLoadingStatus("Building"));
      foreach (var pn in projectNodes)
        await dispatcher.SendStatusAsync(pn.Id, new TestNodeStatus.Building());

      var projectsByPath = projects
          .GroupBy(p => p.ProjectFullPath, StringComparer.OrdinalIgnoreCase)
          .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

      var failedProjectIds = new HashSet<string>();

      await foreach (var result in buildHost.BatchBuildAsync(
          new BatchBuildRequest([.. projectsByPath.Keys], Configuration: null), token.Ct))
      {
        token.Ct.ThrowIfCancellationRequested();
        if (result.Kind != BatchBuildResultKind.Finished) { continue; }

        foreach (var p in projectsByPath.GetValueOrDefault(result.ProjectPath) ?? [])
        {
          var pn = projectNodes.First(n => string.Equals(n.FilePath, p.ProjectFullPath, StringComparison.OrdinalIgnoreCase));
          if (result.Success != true)
          {
            await dispatcher.SendStatusAsync(pn.Id, new TestNodeStatus.BuildFailed(), [TestAction.GetBuildErrors]);
            failedProjectIds.Add(pn.Id);
            if (result.Output?.Diagnostics is { } diags && diags.Length > 0)
            {
              buildErrorStore.Set(pn.Id, diags);
            }

          }
          else
          {
            buildErrorStore.Clear(pn.Id);
          }
        }
      }

      token.Ct.ThrowIfCancellationRequested();

      var runnableProjects = projects
          .Where(p => !failedProjectIds.Contains(
              projectNodes.First(n => string.Equals(n.FilePath, p.ProjectFullPath, StringComparison.OrdinalIgnoreCase)).Id))
          .ToList();

      if (runnableProjects.Count == 0)
      {
        await dispatcher.SendRunnerStatusAsync(BuildIdleStatus());
        return new OperationResult(Success: failedProjectIds.Count == 0);
      }

      var totalLeafCount = runnableProjects
          .Sum(p =>
          {
            var pn = projectNodes.First(n => string.Equals(
          n.FilePath, p.ProjectFullPath, StringComparison.OrdinalIgnoreCase));
            return registry.GetLeafDescendants(pn.Id).Count();
          });

      var sharedCounter = new RunProgressCounter(totalLeafCount);

      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: true,
          CurrentOperation: debug ? "Debugging" : "Running",
          OverallStatus: OverallStatus.Running,
          TotalTests: sharedCounter.TotalTests,
          TotalRunning: sharedCounter.TotalTests,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

      var runTasks = runnableProjects
          .Select(p =>
          {
            var pn = projectNodes.First(n => string.Equals(n.FilePath, p.ProjectFullPath, StringComparison.OrdinalIgnoreCase));
            return executor.RunNodeAsync(pn.Id, p, token, debug, sharedCounter);
          });
      await Task.WhenAll(runTasks);

      var (_, passed, failed, skipped, cancelled) = sharedCounter.Snapshot();
      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: false, CurrentOperation: null,
          OverallStatus: failed > 0 ? OverallStatus.Failed : OverallStatus.Passed,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: passed, TotalFailed: failed,
          TotalSkipped: skipped, TotalCancelled: cancelled));

      return new OperationResult(Success: failed == 0 && failedProjectIds.Count == 0);
    }
    catch (OperationCanceledException)
    {
      await ResetTransientNodesAsync();
      await dispatcher.SendRunnerStatusAsync(BuildIdleStatus());
      return new OperationResult(Success: false);
    }
  }

  private async Task<OperationResult> ExecuteSingleProjectAsync(
      string nodeId, TestNode node, OperationToken token, bool debug)
  {
    var projectId = node.ProjectId
        ?? throw new InvalidOperationException($"Node {nodeId} has no project");

    var project = await ResolveProjectAsync(projectId, token.Ct)
        ?? throw new InvalidOperationException($"Project {projectId} not found");

    try
    {
      await dispatcher.SendRunnerStatusAsync(BuildLoadingStatus("Building"));
      await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.Building());

      var buildFailed = false;
      await foreach (var result in buildHost.BatchBuildAsync(
          new BatchBuildRequest([project.ProjectFullPath], Configuration: null), token.Ct))
      {
        if (result.Kind == BatchBuildResultKind.Finished)
        {
          if (result.Success != true)
          {
            await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.BuildFailed(), [TestAction.GetBuildErrors]);
            buildFailed = true;
            if (result.Output?.Diagnostics is { } diags && diags.Length > 0)
            {
              buildErrorStore.Set(projectId, diags);
            }
          }
          else
          {
            buildErrorStore.Clear(projectId);
          }
        }
      }

      token.Ct.ThrowIfCancellationRequested();

      if (buildFailed)
      {
        await dispatcher.SendRunnerStatusAsync(BuildIdleStatus());
        return new OperationResult(Success: false);
      }

      await dispatcher.SendRunnerStatusAsync(BuildLoadingStatus(debug ? "Debugging" : "Running"));

      var counter = await executor.RunNodeAsync(nodeId, project, token, debug);
      var (_, passed, failed, skipped, cancelled) = counter.Snapshot();

      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: false, CurrentOperation: null,
          OverallStatus: failed > 0 ? OverallStatus.Failed : OverallStatus.Passed,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: passed, TotalFailed: failed,
          TotalSkipped: skipped, TotalCancelled: cancelled));

      return new OperationResult(Success: failed == 0);
    }
    catch (OperationCanceledException)
    {
      await ResetTransientNodesAsync();
      await dispatcher.SendRunnerStatusAsync(BuildIdleStatus());
      return new OperationResult(Success: false);
    }
  }

  private async Task<ValidatedDotnetProject?> ResolveProjectAsync(
      string projectNodeId, CancellationToken ct = default)
  {
    var node = registry.Get(projectNodeId);
    if (node?.FilePath is null || node.TargetFramework is null) return null;
    return await buildHost.GetProjectAsync(node.FilePath, node.TargetFramework, ct: ct);
  }

  private TestRunnerStatus BuildLoadingStatus(string operation) =>
      new(IsLoading: true, CurrentOperation: operation,
          OverallStatus: OverallStatus.Running,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0);

  private TestRunnerStatus BuildIdleStatus() =>
      new(IsLoading: false, CurrentOperation: null,
          OverallStatus: OverallStatus.Idle,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0);

  private async Task ResetTransientNodesAsync()
  {
    var transient = new HashSet<string> { "Building", "Discovering", "Running", "Debugging", "Cancelling" };
    foreach (var node in registry.GetAll())
    {
      if (registry.GetLastStatus(node.Id) is { } s && transient.Contains(s))
        await dispatcher.SendStatusAsync(node.Id, null);
    }
  }

  private static string FormatDuration(long ms) =>
      ms switch
      {
        >= 60_000 => $"{ms / 60_000.0:F1} m",
        >= 1_000 => $"{ms / 1_000.0:F1} s",
        _ => $"{ms} ms"
      };
}

public record InitializeResult(bool Success);
public record OperationResult(bool Success);
public record GetResultsResult(
    bool Found,
    string[]? ErrorMessage,
    string[]? Stdout,
    StackFrameDto[]? Frames,
    StackFrameDto? FailingFrame,
    string? DurationDisplay
);
public record StackFrameDto(string OriginalText, string? File, int? Line, bool IsUserCode);
public record SyncFileRequest(string Path, string Content, int Version);
public record SyncFileResult(LineNumberUpdateDto[] Updates, int Version);
public record LineNumberUpdateDto(string Id, int SignatureLine, int BodyStartLine, int EndLine);