using EasyDotnet.Application.Interfaces;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.TestRunner.Adapters;
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
    WorkspaceBuildHostManager buildHost,
    IMsBuildService msBuildService)
{
  // -------------------------------------------------------------------------
  // testrunner/initialize
  // Builds all projects and runs full discovery.
  // This is the only entry point that populates the solution/project nodes.
  // -------------------------------------------------------------------------
  public async Task<InitializeResult> InitializeAsync(string solutionPath, CancellationToken ct)
  {
    using var token = operationLock.TryAcquire("initialize", ct)
        ?? throw new InvalidOperationException("Operation already in progress");

    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: true,
        CurrentOperation: "Initializing",
        OverallStatus: OverallStatus.Building,
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
        LineNumber: null,
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
        TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

    foreach (var project in testProjects)
    {
      token.Ct.ThrowIfCancellationRequested();
      var projectId = NodeIdBuilder.Project(solutionId, project.ProjectName, project.TargetFramework ?? "");
      await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.Building());
      var buildSuccess = await msBuildService.RequestBuildAsync(project.ProjectFullPath, project.TargetFramework, buildArgs: null, configuration: null, token.Ct);
      if (!buildSuccess.Success)
      {
        await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.Failed("", []));
      }
    }

    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: true, CurrentOperation: "Discovering",
        OverallStatus: OverallStatus.Discovering,
        TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

    var discoverTasks = testProjects.Select(project =>
        executor.DiscoverProjectAsync(project, solutionId, token));
    await Task.WhenAll(discoverTasks);

    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: false, CurrentOperation: null,
        OverallStatus: OverallStatus.Idle,
        TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

    return new InitializeResult(Success: true);
  }

  // -------------------------------------------------------------------------
  // testrunner/run
  // -------------------------------------------------------------------------
  public async Task<OperationResult> RunAsync(string nodeId, CancellationToken ct)
      => await ExecuteOnNodeAsync(nodeId, "run", debug: false, ct);

  // -------------------------------------------------------------------------
  // testrunner/debug
  // -------------------------------------------------------------------------
  public async Task<OperationResult> DebugAsync(string nodeId, CancellationToken ct)
      => await ExecuteOnNodeAsync(nodeId, "debug", debug: true, ct);

  // -------------------------------------------------------------------------
  // testrunner/invalidate
  // Rebuilds and rediscovers a subtree.
  // -------------------------------------------------------------------------
  public async Task<OperationResult> InvalidateAsync(string nodeId, CancellationToken ct)
  {
    using var token = operationLock.TryAcquire("invalidate", ct)
        ?? throw new InvalidOperationException("Operation already in progress");

    await dispatcher.SendRunnerStatusAsync(BuildLoadingStatus("Invalidating"));
    await dispatcher.SendBatchStatusAsync(nodeId, null, registry);

    var node = registry.Get(nodeId)
        ?? throw new KeyNotFoundException($"Node {nodeId} not found");

    // Collect all affected project nodes
    var projectIds = node.Type is NodeType.Project
        ? [nodeId]
        : registry.GetDescendants(nodeId)
            .Where(n => n.Type is NodeType.Project)
            .Select(n => n.Id)
            .ToList();

    foreach (var projectId in projectIds)
    {
      var project = await ResolveProjectAsync(projectId, token.Ct);
      if (project is null)
      {
        continue;
      }

      await adapterResolver.InvalidateAsync(project.ProjectFullPath);
      buildHost.InvalidateCache(project.ProjectFullPath);

      await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.Building());
      var buildSuccess = await msBuildService.RequestBuildAsync(project.ProjectFullPath, project.TargetFramework, buildArgs: null, configuration: null, token.Ct);
      if (!buildSuccess.Success)
      {
        await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.Failed("", []));
        continue;
      }
      await executor.DiscoverProjectAsync(project, node.ParentId ?? "", token);
    }

    await dispatcher.SendRunnerStatusAsync(BuildIdleStatus());
    return new OperationResult(Success: true);
  }

  // -------------------------------------------------------------------------
  // testrunner/getResults  (no lock — read-only)
  // -------------------------------------------------------------------------
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

  // -------------------------------------------------------------------------
  // Shared helpers
  // -------------------------------------------------------------------------

  private async Task<OperationResult> ExecuteOnNodeAsync(
      string nodeId, string opName, bool debug, CancellationToken ct)
  {
    using var token = operationLock.TryAcquire(opName, ct)
        ?? throw new InvalidOperationException("Operation already in progress");

    await dispatcher.SendRunnerStatusAsync(BuildLoadingStatus(
        debug ? "Debugging" : "Running"));

    var node = registry.Get(nodeId) ?? throw new KeyNotFoundException($"Node {nodeId} not found");

    //TODO: solution
    var projectId = node.ProjectId ?? throw new InvalidOperationException($"Node {nodeId} has no project");

    var project = await ResolveProjectAsync(projectId, token.Ct) ?? throw new InvalidOperationException($"Project {projectId} not found");

    await executor.RunNodeAsync(nodeId, project, token, debug);

    var (passed, failed, skipped) = BuildSummary();
    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: false, CurrentOperation: null,
        OverallStatus: failed > 0 ? OverallStatus.Failed : OverallStatus.Passed,
        TotalPassed: passed,
        TotalFailed: failed,
        TotalSkipped: skipped,
        TotalCancelled: 0));

    return new OperationResult(Success: failed == 0);
  }

  private async Task<ValidatedDotnetProject?> ResolveProjectAsync(string projectNodeId, CancellationToken ct = default)
  {
    var node = registry.Get(projectNodeId);
    if (node?.FilePath is null || node.TargetFramework is null)
    {
      return null;
    }


    return await buildHost.GetProjectAsync(node.FilePath, node.TargetFramework, ct: ct);
  }

  private static (int Passed, int Failed, int Skipped) BuildSummary()
  {
    // TODO: track counts incrementally in DetailStore or a separate counter
    return (0, 0, 0);
  }

  private static TestRunnerStatus BuildLoadingStatus(string operation) =>
      new(IsLoading: true, CurrentOperation: operation,
          OverallStatus: OverallStatus.Running,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0);

  private static TestRunnerStatus BuildIdleStatus() =>
      new(IsLoading: false, CurrentOperation: null,
          OverallStatus: OverallStatus.Idle,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0);

  private static string FormatDuration(long ms) =>
      ms switch
      {
        >= 60_000 => $"{ms / 60_000.0:F1} m",
        >= 1_000 => $"{ms / 1_000.0:F1} s",
        _ => $"{ms} ms"
      };
}

// Result DTOs
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