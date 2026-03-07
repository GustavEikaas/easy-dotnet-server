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

namespace EasyDotnet.IDE.TestRunner.Service;

public class TestRunnerService(
    NodeRegistry registry,
    StatusDispatcher dispatcher,
    DetailStore detailStore,
    GlobalOperationLock operationLock,
    OperationExecutor executor,
    AdapterResolver adapterResolver,
    WorkspaceBuildHostManager buildHost)
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
    using var token = operationLock.TryAcquire("initialize", ct)
        ?? throw new InvalidOperationException("Operation already in progress");

    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: true,
        CurrentOperation: "Initializing",
        OverallStatus: OverallStatus.Building,
        TotalTests: registry.GetLeafCount(), TotalRunning: 0,
        TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

    registry.Clear();
    detailStore.ClearAll();
    await adapterResolver.InvalidateAllAsync();

    var solutionName = Path.GetFileName(solutionPath);
    var solutionId = NodeIdBuilder.Solution(solutionName);
    var solutionNode = new TestNode(
        Id: solutionId,
        DisplayName: solutionName,
        ParentId: null,
        FilePath: solutionPath,
        SignatureLine: null,
        BodyStartLine: null,
        EndLine: null,
        Type: new NodeType.Solution(),
        ProjectId: null,
        AvailableActions: [TestAction.Run, TestAction.Invalidate]
    );
    registry.Register(solutionNode);
    await dispatcher.SendRegisterTestAsync(solutionNode);

    var testProjects = await buildHost.GetTestProjectsFromSolutionAsync(solutionPath, ct: token.Ct);

    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: true, CurrentOperation: "Building",
        OverallStatus: OverallStatus.Building,
        TotalTests: registry.GetLeafCount(), TotalRunning: 0,
        TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

    var projectsByPath = testProjects
        .GroupBy(p => p.ProjectFullPath, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    foreach (var variants in projectsByPath.Values)
    {
      foreach (var project in variants)
      {
        var projectId = NodeIdBuilder.Project(solutionId, project.ProjectName, project.TargetFramework ?? "");
        var projectNode = new TestNode(
            Id: projectId,
            DisplayName: $"{project.ProjectName} ({project.TargetFramework})",
            ParentId: solutionId,
            FilePath: project.ProjectFullPath,
            SignatureLine: null, BodyStartLine: null, EndLine: null,
            Type: new NodeType.Project(),
            ProjectId: projectId,
            AvailableActions: [TestAction.Run, TestAction.Debug, TestAction.Invalidate]
        );
        registry.Register(projectNode);
        await dispatcher.SendRegisterTestAsync(projectNode);
      }
    }

    var buildRequest = new BatchBuildRequest(
        ProjectPaths: [.. projectsByPath.Keys],
        Configuration: null);

    var discoverTasks = new List<Task>();

    try
    {
      await foreach (var result in buildHost.BatchBuildAsync(buildRequest, token.Ct))
      {
        token.Ct.ThrowIfCancellationRequested();

        var tfmVariants = projectsByPath.GetValueOrDefault(result.ProjectPath);
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
            await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.Failed("", []));
            continue;
          }

          discoverTasks.Add(
              executor.DiscoverProjectAsync(project, solutionId, token));
        }
      }

      token.Ct.ThrowIfCancellationRequested();

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
            await dispatcher.SendStatusAsync(pid, new TestNodeStatus.Failed("", []));
            continue;
          }

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

  public GetResultsResult GetResults(string nodeId)
  {
    var detail = detailStore.Get(nodeId);
    if (detail is null)
      return new GetResultsResult(Found: false, null, null, null, null, null);

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
        updates.Add(new LineNumberUpdateDto(
            node.Id, loc.SignatureLine, loc.BodyStartLine, loc.EndLine));
    }

    return new SyncFileResult(updates.ToArray(), req.Version);
  }

  private async Task<OperationResult> ExecuteOnNodeAsync(
      string nodeId, string opName, bool debug, CancellationToken ct)
  {
    using var token = operationLock.TryAcquire(opName, ct)
        ?? throw new InvalidOperationException("Operation already in progress");

    var node = registry.Get(nodeId)
        ?? throw new KeyNotFoundException($"Node {nodeId} not found");

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
        if (result.Kind == BatchBuildResultKind.Finished && result.Success != true)
        {
          await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.Failed("", []));
          buildFailed = true;
        }
      }

      token.Ct.ThrowIfCancellationRequested();

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
          TotalTests: counter.TotalTests,
          TotalRunning: 0,
          TotalPassed: passed,
          TotalFailed: failed,
          TotalSkipped: skipped,
          TotalCancelled: cancelled));

      return new OperationResult(Success: failed == 0);
    }
    catch (OperationCanceledException)
    {
      await ResetTransientNodesAsync();
      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: false, CurrentOperation: null,
          OverallStatus: OverallStatus.Idle,
          TotalTests: registry.GetLeafCount(),
          TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

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