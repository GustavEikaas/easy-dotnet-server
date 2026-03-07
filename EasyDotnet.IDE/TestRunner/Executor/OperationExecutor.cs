using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.TestRunner.Adapters;
using EasyDotnet.IDE.TestRunner.Analysis;
using EasyDotnet.IDE.TestRunner.Dispatch;
using EasyDotnet.IDE.TestRunner.Lock;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;
using EasyDotnet.IDE.TestRunner.Store;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.TestRunner.Executor;

/// <summary>
/// Executes the multi-step pipeline for each operation type.
/// Emits registerTest and updateStatus notifications as work progresses.
/// TestRunnerService stays thin — all sequencing lives here.
/// </summary>
public class OperationExecutor(
    NodeRegistry registry,
    StatusDispatcher dispatcher,
    DetailStore detailStore,
    AdapterResolver adapterResolver,
    ILogger<OperationExecutor> logger)
{
  public async Task DiscoverProjectAsync(ValidatedDotnetProject project, string solutionNodeId, OperationToken token)
  {
    var projectNodeId = EnsureProjectNode(project, solutionNodeId);
    await dispatcher.SendStatusAsync(projectNodeId, new TestNodeStatus.Discovering());

    registry.ClearDescendants(projectNodeId);

    var adapter = adapterResolver.Resolve(project);
    var emittedNamespaces = new HashSet<string>();
    var emittedClasses = new HashSet<string>();
    var emittedTheoryGroups = new HashSet<string>();
    var rootNs = project.Raw.RootNamespace ?? project.ProjectName;
    var rootNamespaceParts = rootNs.Split('.', StringSplitOptions.RemoveEmptyEntries);

    var locator = new TestSourceLocator();
    var pendingEmit = new List<TestNode>();

    try
    {
      await adapter.DiscoverAsync(project, async discovered =>
      {
        token.Ct.ThrowIfCancellationRequested();

        var namespaceParts = StripRootNamespace(discovered.NamespaceParts, rootNamespaceParts);

        var namespaceNodeId = namespaceParts.Count > 0
                        ? await EnsureNamespaceChainAsync(
                            projectNodeId, discovered.NamespaceParts,
                            namespaceParts, emittedNamespaces, pendingEmit, token.Ct)
                        : projectNodeId;

        var parentId = namespaceNodeId;
        if (discovered.ClassName is not null)
        {
          var classNodeId = NodeIdBuilder.Class(namespaceNodeId, discovered.ClassName);
          if (emittedClasses.Add(classNodeId))
          {
            var classLocation = locator.LocateClass(discovered.FilePath, discovered.ClassName);
            var classNode = new TestNode(
                    Id: classNodeId,
                    DisplayName: discovered.ClassName,
                    ParentId: namespaceNodeId,
                    FilePath: discovered.FilePath,
                    SignatureLine: classLocation?.SignatureLine,
                    BodyStartLine: classLocation?.BodyStartLine,
                    EndLine: classLocation?.EndLine,
                    Type: new NodeType.TestClass(),
                    ProjectId: projectNodeId,
                    AvailableActions: [TestAction.Run, TestAction.Debug, TestAction.GoToSource]
                );
            registry.Register(classNode);
            pendingEmit.Add(classNode);
          }
          parentId = classNodeId;
        }

        var shortMethodName = discovered.MethodName.Contains('.')
            ? discovered.MethodName[(discovered.MethodName.LastIndexOf('.') + 1)..]
            : discovered.MethodName;

        var location = locator.Locate(discovered.FilePath, shortMethodName);

        if (discovered.Arguments is not null)
        {
          var groupId = NodeIdBuilder.TheoryGroup(parentId, shortMethodName);
          if (emittedTheoryGroups.Add(groupId))
          {
            var groupNode = new TestNode(
                    Id: groupId,
                    DisplayName: shortMethodName,
                    ParentId: parentId,
                    FilePath: discovered.FilePath,
                    SignatureLine: location?.SignatureLine,
                    BodyStartLine: location?.BodyStartLine,
                    EndLine: location?.EndLine,
                    Type: new NodeType.TheoryGroup(),
                    ProjectId: projectNodeId,
                    AvailableActions: discovered.FilePath is not null
                        ? [TestAction.Run, TestAction.GoToSource]
                        : [TestAction.Run]
                );
            registry.Register(groupNode);
            pendingEmit.Add(groupNode);
          }
          parentId = groupId;
        }

        var shortName = discovered.Arguments is not null
            ? discovered.Arguments
            : shortMethodName;

        var methodNodeId = discovered.Arguments is not null
            ? NodeIdBuilder.Method(parentId, shortMethodName + discovered.Arguments)
            : NodeIdBuilder.Method(parentId, discovered.MethodName);
        var methodNode = new TestNode(
                Id: methodNodeId,
                DisplayName: shortName,
                ParentId: parentId,
                FilePath: discovered.FilePath,
                SignatureLine: location?.SignatureLine,
                BodyStartLine: location?.BodyStartLine,
                EndLine: location?.EndLine,
                Type: discovered.Arguments is not null
                    ? new NodeType.Subcase()
                    : new NodeType.TestMethod(),
                ProjectId: projectNodeId,
                AvailableActions: discovered.FilePath is not null
                    ? [TestAction.Run, TestAction.Debug, TestAction.GoToSource]
                    : [TestAction.Run, TestAction.Debug]
            );
        registry.Register(methodNode, nativeId: discovered.NativeId);
        pendingEmit.Add(methodNode);

      }, token.Ct);

      CollapseNamespaces(projectNodeId, pendingEmit);
      foreach (var node in pendingEmit)
        await dispatcher.SendRegisterTestAsync(node);

      await dispatcher.SendStatusAsync(projectNodeId, new TestNodeStatus.Idle());
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Discovery failed for project {Project} — DLL may be missing or stale", project.ProjectName);
      await dispatcher.SendStatusAsync(projectNodeId, null);
    }
  }

  private static IReadOnlyList<string> StripRootNamespace(
         IReadOnlyList<string> parts,
         string[] rootParts)
  {
    if (parts.Count < rootParts.Length) return parts;

    for (var i = 0; i < rootParts.Length; i++)
    {
      if (!string.Equals(parts[i], rootParts[i], StringComparison.OrdinalIgnoreCase))
        return parts;
    }

    return parts.Skip(rootParts.Length).ToList();
  }

  public async Task<RunProgressCounter> RunNodeAsync(
    string nodeId,
    ValidatedDotnetProject project,
    OperationToken token,
    bool debug = false)
  {
    detailStore.ClearSubtree(registry.GetDescendants(nodeId).Select(n => n.Id).Append(nodeId));

    var leafNodes = registry.GetLeafDescendants(nodeId).ToList();
    var self = registry.Get(nodeId);
    if (self?.Type is NodeType.TestMethod or NodeType.Subcase)
      leafNodes.Add(self);

    var runningStatus = debug ? (TestNodeStatus)new TestNodeStatus.Debugging() : new TestNodeStatus.Running();
    await dispatcher.SendBatchStatusAsync(nodeId, runningStatus, registry);

    var nativeIds = leafNodes
        .Select(n => registry.GetNativeId(n.Id))
        .Where(id => id is not null)
        .Select(id => id!)
        .ToList();

    if (nativeIds.Count == 0)
    {
      logger.LogWarning("No runnable tests found under node {NodeId}", nodeId);
      return new RunProgressCounter(0);
    }

    var counter = new RunProgressCounter(nativeIds.Count);
    await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
              IsLoading: true,
              CurrentOperation: debug ? "Debugging" : "Running",
              OverallStatus: OverallStatus.Running,
              TotalTests: counter.TotalTests,
              TotalRunning: counter.TotalTests,
              TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0, TotalCancelled: 0));

    var adapter = adapterResolver.Resolve(project);
    var leafIds = leafNodes.Select(n => n.Id).ToHashSet();

    Func<TestRunResult, Task> onResult = async result =>
    {
      var stableId = registry.GetStableId(result.NativeId);
      if (stableId is null)
      {
        logger.LogWarning("Received result for unknown native ID {NativeId}", result.NativeId);
        return;
      }

      detailStore.Set(stableId, new TestDetail(
              Outcome: result.Outcome,
              ErrorMessage: result.ErrorMessage,
              DurationMs: result.DurationMs,
              Frames: result.Frames,
              FailingFrame: result.FailingFrame,
              Stdout: result.Stdout
          ));

      counter.Record(result.Outcome);
      var (running, passed, failed, skipped, cancelled) = counter.Snapshot();
      await dispatcher.SendRunnerStatusAsync(new TestRunnerStatus(
                   IsLoading: true,
                   CurrentOperation: debug ? "Debugging" : "Running",
                   OverallStatus: OverallStatus.Running,
                   TotalTests: counter.TotalTests,
                   TotalRunning: running,
                   TotalPassed: passed,
                   TotalFailed: failed,
                   TotalSkipped: skipped,
                   TotalCancelled: cancelled));

      var (status, actions) = BuildStatusAndActions(result);
      await dispatcher.SendStatusAsync(stableId, status, actions);
      await BubbleStatusAsync(stableId, leafIds);
    };

    if (debug)
      await adapter.DebugAsync(project, nativeIds, onResult, token.Ct);
    else
      await adapter.RunAsync(project, nativeIds, onResult, token.Ct);

    return counter;
  }

  private string EnsureProjectNode(ValidatedDotnetProject project, string solutionNodeId)
  {
    var projectNodeId = NodeIdBuilder.Project(
        solutionNodeId, project.ProjectName, project.TargetFramework ?? "");

    if (!registry.Exists(projectNodeId))
    {
      var node = new TestNode(
          Id: projectNodeId,
          DisplayName: $"{project.ProjectName} ({project.TargetFramework})",
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
      _ = dispatcher.SendRegisterTestAsync(node);
    }

    return projectNodeId;
  }

  private async Task<string> EnsureNamespaceChainAsync(
       string projectNodeId,
       IReadOnlyList<string> originalParts,
       IReadOnlyList<string> displayParts,
       HashSet<string> emitted,
       List<TestNode> pendingEmit,
       CancellationToken ct)
  {
    var currentParentId = projectNodeId;
    var skipCount = originalParts.Count - displayParts.Count;

    for (var i = 0; i < originalParts.Count; i++)
    {
      var segmentParts = originalParts.Take(i + 1).ToArray();
      var nsId = NodeIdBuilder.Namespace(projectNodeId, segmentParts);

      if (emitted.Add(nsId))
      {
        if (i >= skipCount)
        {
          var nsNode = new TestNode(
              Id: nsId,
              DisplayName: displayParts[i - skipCount],
              ParentId: currentParentId,
              FilePath: null,
              SignatureLine: null,
              BodyStartLine: null,
              EndLine: null,
              Type: new NodeType.Namespace(),
              ProjectId: projectNodeId,
              AvailableActions: [TestAction.Run, TestAction.Debug]
          );
          registry.Register(nsNode);
          pendingEmit.Add(nsNode);
        }
      }

      if (i >= skipCount)
        currentParentId = nsId;
    }

    return currentParentId;
  }

  /// <summary>
  /// Collapses chains of single-child Namespace nodes into one node with a
  /// dotted display name. The registry keeps all nodes intact for server-side
  /// operations (bubbling, running). Only the client-facing emit list is modified.
  /// </summary>
  private static void CollapseNamespaces(string projectNodeId, List<TestNode> nodes)
  {
    var byId = nodes.ToDictionary(n => n.Id);
    var childrenOf = nodes
        .Where(n => n.ParentId is not null)
        .GroupBy(n => n.ParentId!)
        .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());

    var toRemove = new HashSet<string>();
    var toUpdate = new Dictionary<string, TestNode>();

    void Walk(string parentId)
    {
      var childIds = childrenOf.GetValueOrDefault(parentId) ?? [];
      foreach (var childId in childIds)
      {
        if (!byId.TryGetValue(childId, out var child)) continue;

        if (child.Type is not NodeType.Namespace)
        {
          Walk(childId);
          continue;
        }

        // Walk forward through single-child Namespace chains
        var chain = new List<TestNode> { child };
        var current = child;
        while (true)
        {
          var grandchildIds = childrenOf.GetValueOrDefault(current.Id) ?? [];
          if (grandchildIds.Count == 1
              && byId.TryGetValue(grandchildIds[0], out var grandchild)
              && grandchild.Type is NodeType.Namespace)
          {
            chain.Add(grandchild);
            current = grandchild;
          }
          else break;
        }

        if (chain.Count > 1)
        {
          // Remove all intermediate nodes, update terminal with dotted name + first's parent
          for (var i = 0; i < chain.Count - 1; i++)
            toRemove.Add(chain[i].Id);

          var terminal = chain[^1];
          toUpdate[terminal.Id] = terminal with
          {
            DisplayName = string.Join(".", chain.Select(n => n.DisplayName)),
            ParentId = chain[0].ParentId
          };
        }

        Walk(chain[^1].Id);
      }
    }

    Walk(projectNodeId);

    for (var i = nodes.Count - 1; i >= 0; i--)
    {
      var node = nodes[i];
      if (toRemove.Contains(node.Id))
        nodes.RemoveAt(i);
      else if (toUpdate.TryGetValue(node.Id, out var updated))
        nodes[i] = updated;
    }
  }

  private static (TestNodeStatus status, List<TestAction> actions) BuildStatusAndActions(TestRunResult result)
  {
    var baseActions = new List<TestAction> { TestAction.Run, TestAction.Debug, TestAction.GoToSource };
    var hasDetails = result.Frames.Length > 0 || result.Stdout.Length > 0 || result.ErrorMessage.Length > 0;
    if (hasDetails) baseActions.Add(TestAction.PeekResults);

    var duration = result.DurationMs.HasValue
        ? FormatDuration(result.DurationMs.Value)
        : "";

    TestNodeStatus status = result.Outcome switch
    {
      "passed" => new TestNodeStatus.Passed(duration),
      "failed" => new TestNodeStatus.Failed(duration, result.ErrorMessage),
      "skipped" => new TestNodeStatus.Skipped(""),
      _ => new TestNodeStatus.Failed(duration, result.ErrorMessage)
    };

    return (status, baseActions);
  }

  private async Task BubbleStatusAsync(string leafId, HashSet<string> runningLeafIds)
  {
    var node = registry.Get(leafId);
    var parentId = node?.ParentId;

    while (parentId is not null)
    {
      var scopedLeaves = registry.GetLeafDescendants(parentId)
          .Where(n => IsInScope(n.Id, runningLeafIds))
          .ToList();

      logger.LogDebug("Bubble from {LeafId} at parent {ParentId}: scopedLeaves={Count}",
          leafId, parentId, scopedLeaves.Count);

      if (scopedLeaves.Count == 0)
      {
        parentId = registry.Get(parentId)?.ParentId;
        continue;
      }

      var allComplete = scopedLeaves.All(n => detailStore.Get(n.Id) is not null);
      if (!allComplete)
      {
        await dispatcher.SendStatusAsync(parentId, new TestNodeStatus.Running());
        parentId = registry.Get(parentId)?.ParentId;
        continue;
      }

      var hasFailed = scopedLeaves.Any(n => detailStore.Get(n.Id)?.Outcome == "failed");
      var hasSkipped = !hasFailed && scopedLeaves.Any(n => detailStore.Get(n.Id)?.Outcome == "skipped");

      logger.LogDebug("Bubble parent {ParentId}: hasFailed={HasFailed} hasSkipped={HasSkipped}",
          parentId, hasFailed, hasSkipped);

      var maxDuration = scopedLeaves
          .Select(n => detailStore.Get(n.Id)?.DurationMs ?? 0)
          .DefaultIfEmpty(0)
          .Max();
      var durationDisplay = FormatDuration(maxDuration);

      TestNodeStatus aggregateStatus = hasFailed
          ? new TestNodeStatus.Failed(durationDisplay, [])
          : hasSkipped
              ? new TestNodeStatus.Skipped("")
              : new TestNodeStatus.Passed(durationDisplay);

      await dispatcher.SendStatusAsync(parentId, aggregateStatus);
      parentId = registry.Get(parentId)?.ParentId;
    }
  }

  private bool IsInScope(string leafId, HashSet<string> runningLeafIds)
  {
    if (runningLeafIds.Contains(leafId)) return true;
    var n = registry.Get(leafId);
    while (n?.ParentId is not null)
    {
      if (runningLeafIds.Contains(n.ParentId)) return true;
      n = registry.Get(n.ParentId);
    }
    return false;
  }

  private static string FormatDuration(long ms) =>
      ms switch
      {
        >= 60_000 => $"{ms / 60_000.0:F1} m",
        >= 1_000 => $"{ms / 1_000.0:F1} s",
        _ => $"{ms} ms"
      };
}