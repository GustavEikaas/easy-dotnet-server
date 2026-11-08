using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Notifications;
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
    IClientService clientService,
    ILogger<TestRunnerService> logger)
{
  private readonly object _opSync = new();
  private CancellationTokenSource? _operationCts;
  private OperationControl? _operationControl;
  private long _operationId;
  private OperationStage _operationStage = OperationStage.Idle;
  private volatile bool _isInitialized = false;

  private enum OperationStage
  {
    Idle,
    Running,
    Cancelling,
    Killing
  }

  public async Task QuickDiscoverAsync(string solutionPath, CancellationToken ct)
  {
    var opCts = new CancellationTokenSource();
    var control = new OperationControl();

    using var token = operationLock.TryAcquire("quickDiscover", ct, opCts.Token);
    if (token is null)
    {
      opCts.Dispose();
      return;
    }

    TrackOperationStart(token, opCts, control);

    try
    {
      _isInitialized = false;
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
      await dispatcher.SendRegisterTestAsync(solutionNode, token.OperationId);

      var testProjects = await buildHost.GetTestProjectsFromSolutionAsync(solutionPath, ct: token.Ct);

      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: true, CurrentOperation: "Discovering",
          OverallStatus: OverallStatus.Discovering,
          TotalTests: 0, TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0), token.OperationId);

      var discoverTasks = testProjects.Select(project =>
          executor.DiscoverProjectAsync(project, solutionId, control, token));

      await Task.WhenAll(discoverTasks);

      token.Ct.ThrowIfCancellationRequested();

      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: false, CurrentOperation: null,
          OverallStatus: OverallStatus.Idle,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0), token.OperationId);
    }
    catch (OperationCanceledException)
    {
      await ResetTransientNodesAsync(token.OperationId, markCancelled: true);
      await dispatcher.SendRunnerStatusAsync(BuildCancelledStatus(), token.OperationId);
    }
    finally
    {
      TrackOperationEnd(token);
    }
  }

  public async Task<InitializeResult> InitializeAsync(string solutionPath, CancellationToken ct)
  {
    if (_isInitialized) return new InitializeResult(Success: true);

    var opCts = new CancellationTokenSource();
    var control = new OperationControl();

    using var token = await operationLock.WaitAcquireAsync("initialize", ct, opCts.Token);
    TrackOperationStart(token, opCts, control);

    if (_isInitialized)
    {
      TrackOperationEnd(token);
      return new InitializeResult(Success: true);
    }

    var solutionName = Path.GetFileName(solutionPath);
    var solutionId = NodeIdBuilder.Solution(solutionName);

    try
    {
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
        await dispatcher.SendRegisterTestAsync(solutionNode, token.OperationId);
      }

      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: true, CurrentOperation: "Restoring",
          OverallStatus: OverallStatus.Building,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0), token.OperationId);

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
            var projectId = NodeIdBuilder.Project(solutionId, project);
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
            TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0), token.OperationId);
        _isInitialized = true;
        return new InitializeResult(Success: true);
      }

      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: true, CurrentOperation: "Building",
          OverallStatus: OverallStatus.Building,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0), token.OperationId);

      var buildRequest = new BatchBuildRequest(
          ProjectPaths: [.. needsBuild.Keys],
          Configuration: null);

      var discoverTasks = new List<Task>();

      await foreach (var result in buildHost.BatchBuildAsync(buildRequest, token.Ct))
      {
        token.Ct.ThrowIfCancellationRequested();

        var tfmVariants = needsBuild.GetValueOrDefault(result.ProjectPath);
        if (tfmVariants is null) continue;

        if (result.Kind == BatchBuildResultKind.Started)
        {
          foreach (var project in tfmVariants)
          {
            var projectId = NodeIdBuilder.Project(solutionId, project);
            await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.Building(), operationId: token.OperationId);
          }
          continue;
        }

        foreach (var project in tfmVariants)
        {
          var projectId = NodeIdBuilder.Project(solutionId, project);

          if (result.Success != true)
          {
            await dispatcher.SendStatusAsync(projectId, new TestNodeStatus.BuildFailed(),
                [TestAction.GetBuildErrors, TestAction.Invalidate, TestAction.Debug, TestAction.Run],
                token.OperationId);

            if (result.Output?.Diagnostics is { } initDiags && initDiags.Length > 0)
            {
              buildErrorStore.Set(projectId, initDiags);
            }

            continue;
          }

          buildErrorStore.Clear(projectId);
          discoverTasks.Add(executor.DiscoverProjectAsync(project, solutionId, control, token));
        }
      }

      token.Ct.ThrowIfCancellationRequested();

      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: true, CurrentOperation: "Discovering",
          OverallStatus: OverallStatus.Discovering,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0), token.OperationId);

      await Task.WhenAll(discoverTasks);

      token.Ct.ThrowIfCancellationRequested();

      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
          IsLoading: false, CurrentOperation: null,
          OverallStatus: OverallStatus.Idle,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0), token.OperationId);

      _isInitialized = true;
      return new InitializeResult(Success: true);
    }
    catch (OperationCanceledException)
    {
      await ResetTransientNodesAsync(token.OperationId, markCancelled: true);
      await dispatcher.SendRunnerStatusAsync(BuildCancelledStatus(), token.OperationId);
      return new InitializeResult(Success: false);
    }
    finally
    {
      TrackOperationEnd(token);
    }
  }

  public async Task<OperationResult> RunAsync(IReadOnlyList<string> nodeIds, string? source, CancellationToken ct)
      => await ExecuteOnNodesAsync(nodeIds, "run", debug: false, source, ct);

  public async Task<OperationResult> DebugAsync(IReadOnlyList<string> nodeIds, string? source, CancellationToken ct)
      => await ExecuteOnNodesAsync(nodeIds, "debug", debug: true, source, ct);

  public async Task CancelAsync(CancellationToken ct)
  {
    (OperationStage Stage, long Id, CancellationTokenSource Cts, OperationControl Control)? snap;

    lock (_opSync)
    {
      if (_operationCts is null || _operationControl is null || _operationId == 0 || _operationStage == OperationStage.Idle)
        return;

      snap = (_operationStage, _operationId, _operationCts, _operationControl);

      _operationStage = _operationStage switch
      {
        OperationStage.Running => OperationStage.Cancelling,
        OperationStage.Cancelling => OperationStage.Killing,
        _ => _operationStage
      };
    }

    var (stage, operationId, cts, control) = snap!.Value;

    if (stage == OperationStage.Running)
    {
      await dispatcher.SendRunnerStatusAsync(BuildCancellingStatus(), operationId);
      try { cts.Cancel(); } catch { }
      return;
    }

    if (stage != OperationStage.Cancelling)
    {
      return;
    }

    await dispatcher.SendRunnerStatusAsync(BuildKillingStatus(), operationId);
    try { cts.Cancel(); } catch { }

    try { await control.KillAsync(timeout: TimeSpan.FromSeconds(2)); } catch { }

    var released = await WaitForUnlockAsync(operationId, timeout: TimeSpan.FromSeconds(2), ct);

    if (!released)
    {
      operationLock.ForceReleaseIfHeld();

      lock (_opSync)
      {
        if (_operationId == operationId)
        {
          _operationCts = null;
          _operationControl = null;
          _operationId = 0;
          _operationStage = OperationStage.Idle;
        }
      }
    }

    await ResetTransientNodesAsync(operationId: null, markCancelled: true);
    await dispatcher.SendRunnerStatusAsync(BuildKilledStatus(), operationId: null);

    return;
  }

  private async Task<bool> WaitForUnlockAsync(long operationId, TimeSpan timeout, CancellationToken ct)
  {
    var stopAt = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < stopAt)
    {
      if (!operationLock.IsLocked) return true;
      if (operationLock.CurrentOperationId != operationId) return true;
      await Task.Delay(50, ct);
    }

    return !operationLock.IsLocked || operationLock.CurrentOperationId != operationId;
  }

  public async Task<OperationResult> InvalidateAsync(string nodeId, CancellationToken ct)
  {
    var opCts = new CancellationTokenSource();
    var control = new OperationControl();

    using var token = operationLock.TryAcquire("invalidate", ct, opCts.Token)
        ?? throw new InvalidOperationException("Operation already in progress");

    TrackOperationStart(token, opCts, control);

    try
    {
      await dispatcher.SendRunnerStatusAsync(BuildLoadingStatus("Invalidating", OverallStatus.Building), token.OperationId);
      await dispatcher.SendBatchStatusAsync(nodeId, null, registry, operationId: token.OperationId);

      var node = registry.Get(nodeId)
          ?? throw new KeyNotFoundException($"Node {nodeId} not found");

      if (node.Type is NodeType.Solution)
      {
        return await InvalidateSolutionAsync(node, control, token);
      }

      if (node.Type is not NodeType.Project)
      {
        throw new InvalidOperationException($"Invalidate not supported for node type: {node.Type}");
      }

      var solutionNodeId = node.ParentId ?? throw new InvalidOperationException($"Project node {nodeId} has no parent solution");

      var projectsByPath = new Dictionary<string, List<ValidatedDotnetProject>>(
          StringComparer.OrdinalIgnoreCase);

      var project = await ResolveProjectAsync(nodeId, token.Ct);
      if (project is not null)
      {
        await adapterResolver.InvalidateAsync(project.ProjectFullPath);
        projectsByPath[project.ProjectFullPath] = [project];
      }

      if (projectsByPath.Count == 0)
      {
        await dispatcher.SendRunnerStatusAsync(BuildIdleStatus(), token.OperationId);
        return new OperationResult(Success: true);
      }

      var singlePath = projectsByPath.Keys.Single();
      var tfm = await buildHost.ResolveSingleTfmAsync(singlePath, "Debug", token.Ct);
      var buildRequest = new BatchBuildRequest(
          ProjectPaths: [singlePath],
          Configuration: "Debug",
          TargetFramework: tfm);

      var discoverTasks = new List<Task>();

      await foreach (var result in buildHost.BatchBuildAsync(buildRequest, token.Ct))
      {
        token.Ct.ThrowIfCancellationRequested();

        var variants = projectsByPath.GetValueOrDefault(result.ProjectPath);
        if (variants is null) continue;

        if (result.Kind == BatchBuildResultKind.Started)
        {
          foreach (var p in variants)
          {
            var pid = NodeIdBuilder.Project(solutionNodeId, p);
            await dispatcher.SendStatusAsync(pid, new TestNodeStatus.Building(), operationId: token.OperationId);
          }
          continue;
        }

        foreach (var p in variants)
        {
          var pid = NodeIdBuilder.Project(solutionNodeId, p);
          if (result.Success != true)
          {
            await dispatcher.SendStatusAsync(pid, new TestNodeStatus.BuildFailed(),
                [TestAction.GetBuildErrors, TestAction.Invalidate, TestAction.Debug, TestAction.Run],
                token.OperationId);

            if (result.Output?.Diagnostics is { } invDiags && invDiags.Length > 0)
            {
              buildErrorStore.Set(pid, invDiags);
            }
            continue;
          }

          buildErrorStore.Clear(pid);
          discoverTasks.Add(executor.DiscoverProjectAsync(p, solutionNodeId, control, token));
        }
      }

      token.Ct.ThrowIfCancellationRequested();
      await Task.WhenAll(discoverTasks);

      token.Ct.ThrowIfCancellationRequested();

      await dispatcher.SendRunnerStatusAsync(BuildIdleStatus(), token.OperationId);
      return new OperationResult(Success: true);
    }
    catch (OperationCanceledException)
    {
      await ResetTransientNodesAsync(token.OperationId, markCancelled: true);
      await dispatcher.SendRunnerStatusAsync(BuildCancelledStatus(), token.OperationId);
      return new OperationResult(Success: false);
    }
    finally
    {
      TrackOperationEnd(token);
    }
  }

  private async Task<OperationResult> InvalidateSolutionAsync(TestNode solutionNode, OperationControl control, OperationToken token)
  {
    if (solutionNode.FilePath is null)
    {
      throw new InvalidOperationException($"Solution node {solutionNode.Id} has no solution path");
    }

    var solutionNodeId = solutionNode.Id;
    var solutionPath = solutionNode.FilePath;

    await dispatcher.SendRunnerStatusAsync(BuildLoadingStatus("Restoring", OverallStatus.Building), token.OperationId);

    try
    {
      await foreach (var result in buildHost.RestoreNugetPackagesSolutionAsync(solutionPath, token.Ct))
      {
        if (!result.Success)
        {
          logger.LogWarning("Restore failed for {Path}: {Error}", result.ProjectPath, result.ErrorMessage);
        }
      }
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Optimistic restore failed — continuing anyway");
    }

    await adapterResolver.InvalidateAllAsync();

    var testProjects = await buildHost.GetTestProjectsFromSolutionAsync(solutionPath, ct: token.Ct);

    var desiredProjectIds = testProjects
        .Select(p => NodeIdBuilder.Project(solutionNodeId, p))
        .ToHashSet(StringComparer.Ordinal);

    var existingProjectNodes = registry.GetDescendants(solutionNodeId)
        .Where(n => n.Type is NodeType.Project)
        .ToList();

    foreach (var pn in existingProjectNodes)
    {
      if (desiredProjectIds.Contains(pn.Id)) continue;

      buildErrorStore.Clear(pn.Id);
      var removedIds = registry.RemoveSubtree(pn.Id);
      detailStore.ClearSubtree(removedIds);

      foreach (var id in removedIds)
      {
        await dispatcher.SendRemoveTestAsync(id, token.OperationId);
      }
    }

    foreach (var p in testProjects)
    {
      await EnsureProjectNodeAsync(p, solutionNodeId, token.OperationId);
    }

    var projectsByPath = testProjects
        .GroupBy(p => p.ProjectFullPath, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    if (projectsByPath.Count == 0)
    {
      await dispatcher.SendRunnerStatusAsync(BuildIdleStatus(), token.OperationId);
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
        foreach (var p in variants)
        {
          var pid = NodeIdBuilder.Project(solutionNodeId, p);
          await dispatcher.SendStatusAsync(pid, new TestNodeStatus.Building(), operationId: token.OperationId);
        }
        continue;
      }

      foreach (var p in variants)
      {
        var pid = NodeIdBuilder.Project(solutionNodeId, p);
        if (result.Success != true)
        {
          await dispatcher.SendStatusAsync(pid, new TestNodeStatus.BuildFailed(),
              [TestAction.GetBuildErrors, TestAction.Invalidate, TestAction.Debug, TestAction.Run],
              token.OperationId);

          if (result.Output?.Diagnostics is { } diags && diags.Length > 0)
          {
            buildErrorStore.Set(pid, diags);
          }
          continue;
        }

        buildErrorStore.Clear(pid);
        discoverTasks.Add(executor.DiscoverProjectAsync(p, solutionNodeId, control, token));
      }
    }

    token.Ct.ThrowIfCancellationRequested();
    await Task.WhenAll(discoverTasks);

    token.Ct.ThrowIfCancellationRequested();

    await dispatcher.SendRunnerStatusAsync(BuildIdleStatus(), token.OperationId);
    return new OperationResult(Success: true);
  }

  private async Task<string> EnsureProjectNodeAsync(ValidatedDotnetProject project, string solutionNodeId, long operationId)
  {
    var projectNodeId = NodeIdBuilder.Project(solutionNodeId, project);

    var existing = registry.Get(projectNodeId);
    if (existing is null)
    {
      var displayName = ProjectNodeDisplayName.For(project);
      var node = new TestNode(
          Id: projectNodeId,
          DisplayName: displayName,
          ParentId: solutionNodeId,
          FilePath: project.ProjectFullPath,
          SignatureLine: null,
          BodyStartLine: null,
          EndLine: null,
          Type: new NodeType.Project(),
          ProjectId: projectNodeId,
          AvailableActions: [TestAction.Run, TestAction.Debug, TestAction.Invalidate],
          TargetFramework: project.TargetFramework);

      registry.Register(node);
      await dispatcher.SendRegisterTestAsync(node, operationId);
      return projectNodeId;
    }

    var displayNameChanged = !string.Equals(existing.DisplayName, ProjectNodeDisplayName.For(project), StringComparison.Ordinal);
    var fileChanged = !string.Equals(existing.FilePath, project.ProjectFullPath, StringComparison.OrdinalIgnoreCase);

    if (fileChanged || displayNameChanged)
    {
      var updated = existing with
      {
        DisplayName = ProjectNodeDisplayName.For(project),
        FilePath = project.ProjectFullPath,
      };

      registry.Register(updated);
      await dispatcher.SendRegisterTestAsync(updated, operationId);
    }

    return projectNodeId;
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

  public List<NeotestPositionDto> GetNeotestPositions(string filePath)
  {
    var fileNodes = registry.GetNodesForFile(filePath).ToList();
    if (fileNodes.Count == 0) return [];

    var group = fileNodes
        .Where(n => n.ProjectId is not null)
        .GroupBy(n => n.ProjectId!)
        .FirstOrDefault();
    if (group is null) return [];

    var nodes = group.ToList();
    var nodeIdSet = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

    return nodes
        .Select(n =>
        {
          var type = n.Type switch
          {
            NodeType.Namespace or
            NodeType.TestClass or
            NodeType.TheoryGroup => "namespace",
            NodeType.TestMethod or
            NodeType.Subcase or
            NodeType.ProbableTest => "test",
            _ => null
          };
          if (type is null) return null;

          var parentId = nodeIdSet.Contains(n.ParentId ?? "") ? n.ParentId : filePath;

          return new NeotestPositionDto(
              Id: n.Id,
              Name: n.DisplayName,
              Type: type,
              ParentId: parentId,
              StartLine: n.SignatureLine,
              EndLine: n.EndLine);
        })
        .OfType<NeotestPositionDto>()
        .OrderBy(dto => dto.StartLine ?? int.MaxValue)
        .ToList();
  }

  public Dictionary<string, NeotestBatchResultDto> GetNeotestBatchResults(string[] ids) =>
      ids
          .Select(id => (id, detail: detailStore.Get(id), node: registry.Get(id)))
          .Where(x => x.detail is not null)
          .ToDictionary(
              x => x.id,
              x => new NeotestBatchResultDto(
                  Outcome: x.detail!.Outcome,
                  ErrorMessage: x.detail.ErrorMessage.Length > 0 ? x.detail.ErrorMessage : null,
                  Frames: [.. x.detail.Frames.Select(f => new StackFrameDto(
                      OriginalText: f.OriginalText,
                      File: f.File,
                      Line: f.Line,
                      IsUserCode: f.IsUserCode))],
                  FailingFrame: x.detail.FailingFrame is { } ff ? StackFrameDto.Create(ff, x.node!) : null,
                  Stdout: x.detail.Stdout.Length > 0 ? x.detail.Stdout : null
                )
              );

  public async Task<SyncFileResult> SyncFileAsync(SyncFileRequest req)
  {
    var parsed = TestSourceLocator.ParseContent(req.Content);
    var updates = new List<LineNumberUpdateDto>();

    var isCSharp = req.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    foreach (var node in registry.GetNodesForFile(req.Path))
    {
      TestMethodLocation? loc;

      if (node.Type is NodeType.TestClass)
      {
        loc = parsed.Classes.TryGetValue(node.DisplayName, out var clsLoc) ? clsLoc : null;
      }
      else
      {
        var lookupName = node.Type switch
        {
          NodeType.Subcase => node.ParentId is not null ? registry.Get(node.ParentId)?.DisplayName : null,
          _ => node.DisplayName
        };

        lookupName ??= node.DisplayName;
        loc = TestSourceLocator.LookupMethod(parsed.Methods, lookupName);
      }

      if (loc is null)
      {
        if (isCSharp && !operationLock.IsLocked)
          registry.RemoveSubtree(node.Id);
        continue;
      }

      registry.UpdateLineNumbers(node.Id, loc.SignatureLine, loc.BodyStartLine, loc.EndLine);
      updates.Add(new LineNumberUpdateDto(node.Id, loc.SignatureLine, loc.BodyStartLine, loc.EndLine));
    }

    var existingProbableIds = registry.GetNodesForFile(req.Path)
        .Where(n => n.Type is NodeType.ProbableTest)
        .Select(n => n.Id)
        .ToHashSet(StringComparer.Ordinal);

    var realNodeLines = registry.GetNodesForFile(req.Path)
        .Where(n => n.Type is not NodeType.ProbableTest && n.SignatureLine.HasValue)
        .Select(n => n.SignatureLine!.Value)
        .ToHashSet();

    var newProbableIds = new HashSet<string>(StringComparer.Ordinal);

    foreach (var probable in parsed.ProbableMethods)
    {
      if (realNodeLines.Contains(probable.Location.SignatureLine))
        continue;

      var classNode = registry.GetNodesForFile(req.Path)
          .FirstOrDefault(n =>
              n.Type is NodeType.TestClass &&
              string.Equals(n.DisplayName, probable.ClassName, StringComparison.Ordinal));

      if (classNode is null) continue;

      var probableId = NodeIdBuilder.Method(classNode.Id, probable.MethodName);

      newProbableIds.Add(probableId);

      var existing = registry.Get(probableId);
      if (existing?.Type is NodeType.ProbableTest)
      {
        registry.UpdateLineNumbers(probableId, probable.Location.SignatureLine, probable.Location.BodyStartLine, probable.Location.EndLine);
        updates.Add(new LineNumberUpdateDto(probableId, probable.Location.SignatureLine, probable.Location.BodyStartLine, probable.Location.EndLine));
        continue;
      }

      var fqn = BuildFqn(classNode, probable.MethodName);

      var probableNode = new TestNode(
          Id: probableId,
          DisplayName: probable.MethodName,
          ParentId: classNode.Id,
          FilePath: req.Path,
          SignatureLine: probable.Location.SignatureLine,
          BodyStartLine: probable.Location.BodyStartLine,
          EndLine: probable.Location.EndLine,
          Type: new NodeType.ProbableTest(),
          ProjectId: classNode.ProjectId,
          AvailableActions: [TestAction.Run, TestAction.Debug, TestAction.GoToSource]
      );

      registry.Register(probableNode, nativeId: fqn);
      await dispatcher.SendRegisterTestAsync(probableNode);

      updates.Add(new LineNumberUpdateDto(probableId, probable.Location.SignatureLine, probable.Location.BodyStartLine, probable.Location.EndLine));
    }

    foreach (var staleId in existingProbableIds)
    {
      if (newProbableIds.Contains(staleId)) continue;
      registry.RemoveSubtree(staleId);
      await dispatcher.SendRemoveTestAsync(staleId);
    }

    return new SyncFileResult([.. updates], req.Version);
  }

  /// <summary>
  /// Walks from a class node upward through Namespace nodes and builds the
  /// dotted FQN used as the probable node's nativeId.
  /// </summary>
  private string BuildFqn(TestNode classNode, string methodName)
  {
    var segments = new Stack<string>();
    segments.Push(methodName);
    segments.Push(classNode.DisplayName);

    var current = classNode.ParentId is not null ? registry.Get(classNode.ParentId) : null;
    while (current is not null && current.Type is NodeType.Namespace)
    {
      segments.Push(current.DisplayName);
      current = current.ParentId is not null ? registry.Get(current.ParentId) : null;
    }

    return string.Join(".", segments);
  }

  /// <summary>
  /// If <paramref name="requested"/> was a ProbableTest that has now been replaced
  /// (same FilePath + ParentId + DisplayName) by a real TestMethod / TheoryGroup under a
  /// different stable id (adapters disagree on whether <c>discovered.MethodName</c> is
  /// short or FQN), returns the successor's id. Otherwise returns the requested id.
  /// <paramref name="requested"/> must be the node as it was before rediscovery.
  /// </summary>
  private string ResolveProbableSuccessor(TestNode requested)
  {
    if (requested.Type is not NodeType.ProbableTest) return requested.Id;
    if (requested.FilePath is null || requested.ParentId is null) return requested.Id;

    var current = registry.Get(requested.Id);
    if (current is not null && current.Type is not NodeType.ProbableTest) return requested.Id;

    var normalized = requested.FilePath.Replace('\\', '/');
    var successor = registry.GetAll().FirstOrDefault(n =>
        (n.Type is NodeType.TestMethod or NodeType.TheoryGroup) &&
        n.ParentId == requested.ParentId &&
        string.Equals(n.DisplayName, requested.DisplayName, StringComparison.Ordinal) &&
        string.Equals(
            n.FilePath?.Replace('\\', '/'),
            normalized,
            StringComparison.OrdinalIgnoreCase));

    return successor?.Id ?? requested.Id;
  }

  private async Task<OperationResult> ExecuteOnNodesAsync(
      IReadOnlyList<string> nodeIds, string opName, bool debug, string? source, CancellationToken ct)
  {
    var opCts = new CancellationTokenSource();
    var control = new OperationControl();

    using var token = operationLock.TryAcquire(opName, ct, opCts.Token)
        ?? throw new InvalidOperationException("Operation already in progress");

    TrackOperationStart(token, opCts, control);

    try
    {
      var targets = await ResolveRunTargetsAsync(nodeIds, token.Ct);
      return await ExecuteRunTargetsAsync(targets, control, token, debug, source);
    }
    catch (OperationCanceledException)
    {
      await ResetTransientNodesAsync(token.OperationId, markCancelled: true);
      await dispatcher.SendRunnerStatusAsync(BuildCancelledStatus(), token.OperationId);
      return new OperationResult(Success: false);
    }
    finally
    {
      TrackOperationEnd(token);
    }
  }

  /// <summary>
  /// Groups the requested nodes by owning project. Unknown ids are skipped, nodes nested
  /// under another requested node are dropped (the ancestor already covers them) and a
  /// Solution node expands to every project beneath it.
  /// </summary>
  private async Task<List<ProjectRunTarget>> ResolveRunTargetsAsync(
      IReadOnlyList<string> nodeIds, CancellationToken ct)
  {
    var requested = new List<TestNode>();
    foreach (var id in nodeIds)
    {
      if (registry.Get(id) is { } node)
        requested.Add(node);
      else
        logger.LogWarning("Skipping unknown node {NodeId}", id);
    }

    if (requested.Count == 0)
    {
      throw new KeyNotFoundException($"Node {string.Join(", ", nodeIds)} not found");
    }

    var requestedIds = requested.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
    var nodesByProject = new Dictionary<string, List<TestNode>>(StringComparer.Ordinal);

    foreach (var node in requested)
    {
      if (HasRequestedAncestor(node, requestedIds)) continue;

      var covered = node.Type is NodeType.Solution
          ? registry.GetDescendants(node.Id).Where(n => n.Type is NodeType.Project)
          : [node];

      foreach (var n in covered)
      {
        var projectId = n.ProjectId
            ?? throw new InvalidOperationException($"Node {n.Id} has no project");

        if (!nodesByProject.TryGetValue(projectId, out var projectNodes))
        {
          projectNodes = [];
          nodesByProject[projectId] = projectNodes;
        }

        projectNodes.Add(n);
      }
    }

    var resolved = await Task.WhenAll(nodesByProject.Select(async kv =>
        (ProjectNodeId: kv.Key, Nodes: kv.Value, Project: await ResolveProjectAsync(kv.Key, ct))));

    foreach (var missing in resolved.Where(r => r.Project is null))
    {
      logger.LogWarning("Skipping project {ProjectId}: project could not be resolved", missing.ProjectNodeId);
    }

    var targets = resolved
        .Where(r => r.Project is not null)
        .Select(r => new ProjectRunTarget(r.ProjectNodeId, r.Project!, r.Nodes))
        .ToList();

    if (targets.Count == 0 && nodesByProject.Count > 0)
    {
      throw new InvalidOperationException($"Project {string.Join(", ", nodesByProject.Keys)} not found");
    }

    return targets;
  }

  private bool HasRequestedAncestor(TestNode node, HashSet<string> requestedIds)
  {
    var parentId = node.ParentId;
    while (parentId is not null)
    {
      if (requestedIds.Contains(parentId)) return true;
      parentId = registry.Get(parentId)?.ParentId;
    }

    return false;
  }

  /// <summary>
  /// Builds every target project in one batch, rediscovers the ones that built, then runs
  /// each project's requested nodes in a single adapter session. Run sessions execute in
  /// parallel across projects; debug sessions execute one project at a time.
  /// </summary>
  private async Task<OperationResult> ExecuteRunTargetsAsync(
      List<ProjectRunTarget> targets,
      OperationControl control,
      OperationToken token,
      bool debug,
      string? source)
  {
    if (targets.Count == 0)
    {
      await dispatcher.SendRunnerStatusAsync(BuildIdleStatus(), token.OperationId);
      return new OperationResult(Success: true);
    }

    await dispatcher.SendRunnerStatusAsync(BuildLoadingStatus("Building", OverallStatus.Building), token.OperationId);
    foreach (var target in targets)
      await dispatcher.SendStatusAsync(target.ProjectNodeId, new TestNodeStatus.Building(), operationId: token.OperationId);

    var targetsByPath = targets
        .GroupBy(t => t.Project.ProjectFullPath, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    var failedProjectIds = new HashSet<string>(StringComparer.Ordinal);

    await foreach (var result in buildHost.BatchBuildAsync(
        new BatchBuildRequest([.. targetsByPath.Keys], Configuration: null), token.Ct))
    {
      token.Ct.ThrowIfCancellationRequested();
      if (result.Kind != BatchBuildResultKind.Finished) { continue; }

      foreach (var target in targetsByPath.GetValueOrDefault(result.ProjectPath) ?? [])
      {
        if (result.Success != true)
        {
          await dispatcher.SendStatusAsync(target.ProjectNodeId, new TestNodeStatus.BuildFailed(),
              [TestAction.GetBuildErrors, TestAction.Invalidate, TestAction.Debug, TestAction.Run],
              token.OperationId);

          failedProjectIds.Add(target.ProjectNodeId);
          if (result.Output?.Diagnostics is { } diags && diags.Length > 0)
          {
            buildErrorStore.Set(target.ProjectNodeId, diags);
          }
        }
        else
        {
          buildErrorStore.Clear(target.ProjectNodeId);
        }
      }
    }

    token.Ct.ThrowIfCancellationRequested();

    var runnableTargets = targets
        .Where(t => !failedProjectIds.Contains(t.ProjectNodeId))
        .ToList();

    if (runnableTargets.Count == 0)
    {
      await dispatcher.SendRunnerStatusAsync(BuildIdleStatus(), token.OperationId);
      return new OperationResult(Success: false);
    }

    await dispatcher.SendRunnerStatusAsync(BuildLoadingStatus("Discovering", OverallStatus.Discovering), token.OperationId);
    await Task.WhenAll(runnableTargets.Select(t =>
        registry.Get(t.ProjectNodeId)?.ParentId is { } solutionNodeId
            ? executor.DiscoverProjectAsync(t.Project, solutionNodeId, control, token)
            : Task.CompletedTask));

    token.Ct.ThrowIfCancellationRequested();

    var runNodeIdsByProject = runnableTargets.ToDictionary(
        t => t.ProjectNodeId,
        t => (IReadOnlyCollection<string>)[.. t.Nodes.Select(ResolveProbableSuccessor).Distinct(StringComparer.Ordinal)],
        StringComparer.Ordinal);

    var counter = new RunProgressCounter(
        runNodeIdsByProject.Values.Sum(ids => executor.CollectLeaves(ids).Count));

    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: true,
        CurrentOperation: debug ? "Debugging" : "Running",
        OverallStatus: debug ? OverallStatus.Debugging : OverallStatus.Running,
        TotalTests: counter.TotalTests,
        TotalRunning: counter.TotalTests,
        TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0), token.OperationId);

    if (debug)
    {
      foreach (var t in runnableTargets)
        await executor.RunNodesAsync(runNodeIdsByProject[t.ProjectNodeId], t.Project, control, token, debug, counter);
    }
    else
    {
      await Task.WhenAll(runnableTargets.Select(t =>
          executor.RunNodesAsync(runNodeIdsByProject[t.ProjectNodeId], t.Project, control, token, debug, counter)));
    }

    token.Ct.ThrowIfCancellationRequested();

    var (_, passed, failed, skipped, cancelled, inconclusive) = counter.Snapshot();
    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
        IsLoading: false, CurrentOperation: null,
        OverallStatus: failed > 0 ? OverallStatus.Failed : inconclusive > 0 ? OverallStatus.Inconclusive : OverallStatus.Passed,
        TotalTests: registry.GetLeafCount(), TotalRunning: 0,
        TotalPassed: passed, TotalFailed: failed,
        TotalSkipped: skipped, TotalCancelled: cancelled,
        TotalInconclusive: inconclusive), token.OperationId);

    await MaybeNotifyFinishedAsync(source, passed, failed, skipped, cancelled, inconclusive, token.Ct);

    return new OperationResult(Success: failed == 0 && failedProjectIds.Count == 0);
  }

  private sealed record ProjectRunTarget(
      string ProjectNodeId,
      ValidatedDotnetProject Project,
      IReadOnlyList<TestNode> Nodes);

  private async Task<ValidatedDotnetProject?> ResolveProjectAsync(
      string projectNodeId, CancellationToken ct = default)
  {
    var node = registry.Get(projectNodeId);
    if (node?.FilePath is null || node.TargetFramework is null) return null;
    return await buildHost.GetProjectAsync(node.FilePath, node.TargetFramework, ct: ct);
  }

  private TestRunnerStatus BuildLoadingStatus(string operation, OverallStatus overallStatus) =>
      new(IsLoading: true, CurrentOperation: operation,
          OverallStatus: overallStatus,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0);

  private static string NormalizeSource(string? source) =>
      string.IsNullOrWhiteSpace(source) ? "buffer" : source;

  private async Task MaybeNotifyFinishedAsync(
      string? source,
      int passed,
      int failed,
      int skipped,
      int cancelled,
      int inconclusive,
      CancellationToken ct)
  {
    if (clientService.ClientOptions?.EnableOsNotifications != true) return;
    if (NormalizeSource(source) == "buffer") return;

    var total = passed + failed + skipped + cancelled + inconclusive;
    if (total == 0) return;

    bool visible;
    try
    {
      visible = await dispatcher.IsTestRunnerVisibleAsync(ct);
    }
    catch (Exception ex)
    {
      logger.LogDebug(ex, "Failed to query testrunner/isVisible (ignored)");
      return;
    }

    if (visible) return;

    OsNotify.TryNotifyTestRunFinished(logger, passed, failed, skipped, cancelled, inconclusive);
  }

  private TestRunnerStatus BuildIdleStatus() =>
      new(IsLoading: false, CurrentOperation: null,
          OverallStatus: OverallStatus.Idle,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0);

  private TestRunnerStatus BuildCancelledStatus()
  {
    var (passed, failed, skipped, cancelled, inconclusive) = SnapshotLeafTotals();
    return new TestRunnerStatus(
        IsLoading: false, CurrentOperation: null,
        OverallStatus: OverallStatus.Cancelled,
        TotalTests: registry.GetLeafCount(), TotalRunning: 0,
        TotalPassed: passed, TotalFailed: failed, TotalSkipped: skipped, TotalCancelled: cancelled,
        TotalInconclusive: inconclusive);
  }

  private TestRunnerStatus BuildCancellingStatus() =>
      new(IsLoading: true,
          CurrentOperation: null,
          OverallStatus: OverallStatus.Cancelling,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0);

  private TestRunnerStatus BuildKillingStatus() =>
      new(IsLoading: true,
          CurrentOperation: null,
          OverallStatus: OverallStatus.Killing,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0);

  private TestRunnerStatus BuildKilledStatus() =>
      new(IsLoading: false, CurrentOperation: null,
          OverallStatus: OverallStatus.Killed,
          TotalTests: registry.GetLeafCount(), TotalRunning: 0,
          TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0);

  private void TrackOperationStart(OperationToken token, CancellationTokenSource opCts, OperationControl control)
  {
    lock (_opSync)
    {
      _operationCts = opCts;
      _operationControl = control;
      _operationId = token.OperationId;
      _operationStage = OperationStage.Running;
    }
  }

  private void TrackOperationEnd(OperationToken token)
  {
    CancellationTokenSource? toDispose = null;

    lock (_opSync)
    {
      if (_operationId != token.OperationId) return;

      toDispose = _operationCts;
      _operationCts = null;
      _operationControl = null;
      _operationId = 0;
      _operationStage = OperationStage.Idle;
    }

    toDispose?.Dispose();
  }

  private async Task ResetTransientNodesAsync(long? operationId = null, bool markCancelled = false)
  {
    foreach (var node in registry.GetAll())
    {
      if (registry.GetLastStatusKind(node.Id) is not { } kind || !kind.IsTransient()) continue;

      if (markCancelled && kind.IsCancellable())
      {
        await dispatcher.SendStatusAsync(node.Id, new TestNodeStatus.Cancelled(), node.AvailableActions, operationId: operationId);
      }
      else
      {
        await dispatcher.SendStatusAsync(node.Id, null, node.AvailableActions, operationId: operationId);
      }
    }
  }

  private (int Passed, int Failed, int Skipped, int Cancelled, int Inconclusive) SnapshotLeafTotals()
  {
    var passed = 0;
    var failed = 0;
    var skipped = 0;
    var cancelled = 0;
    var inconclusive = 0;

    foreach (var node in registry.GetAll())
    {
      if (node.Type is not (NodeType.TestMethod or NodeType.Subcase)) continue;

      var status = registry.GetLastStatusKind(node.Id);
      switch (status)
      {
        case TestNodeStatusKind.Passed:
          passed++;
          break;
        case TestNodeStatusKind.Failed:
        case TestNodeStatusKind.Faulted:
          failed++;
          break;
        case TestNodeStatusKind.Skipped:
          skipped++;
          break;
        case TestNodeStatusKind.Inconclusive:
          inconclusive++;
          break;
        case TestNodeStatusKind.Cancelled:
          cancelled++;
          break;
      }
    }

    return (passed, failed, skipped, cancelled, inconclusive);
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

public record StackFrameDto(string OriginalText, string? File, int? Line, bool IsUserCode, bool IsFromTest = false)
{
  public static StackFrameDto Create(ParsedStackFrame frame, TestNode node)
  {
    var isFromTest = false;

    if (frame.Line.HasValue && node is { BodyStartLine: not null, EndLine: not null })
    {
      isFromTest = string.Equals(frame.File, node.FilePath, StringComparison.OrdinalIgnoreCase) && frame.Line.Value >= node.BodyStartLine.Value && frame.Line.Value <= node.EndLine.Value;
    }

    return new StackFrameDto(OriginalText: frame.OriginalText, File: frame.File, Line: frame.Line, IsUserCode: frame.IsUserCode, IsFromTest: isFromTest);
  }
}

public record SyncFileRequest(string Path, string Content, int Version);
public record SyncFileResult(LineNumberUpdateDto[] Updates, int Version);
public record LineNumberUpdateDto(string Id, int SignatureLine, int BodyStartLine, int EndLine);

public record NeotestPositionsRequest(string FilePath);
public record NeotestPositionDto(string Id, string Name, string Type, string? ParentId, int? StartLine, int? EndLine);
public record NeotestBatchResultsRequest(string[] Ids);
public record NeotestBatchResultDto(string Outcome, string[]? ErrorMessage, StackFrameDto[]? Frames, StackFrameDto? FailingFrame, string[]? Stdout);