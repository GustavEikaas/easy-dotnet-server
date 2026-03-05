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
    var rootNs = project.Raw.RootNamespace ?? project.ProjectName;
    var rootNamespaceParts = rootNs.Split('.', StringSplitOptions.RemoveEmptyEntries);
    var locator = new TestSourceLocator();
    await adapter.DiscoverAsync(project, async discovered =>
    {
      token.Ct.ThrowIfCancellationRequested();

      var namespaceParts = StripRootNamespace(discovered.NamespaceParts, rootNamespaceParts);

      var namespaceNodeId = namespaceParts.Count > 0
                      ? await EnsureNamespaceChainAsync(projectNodeId, discovered.NamespaceParts, namespaceParts, emittedNamespaces)
                      : projectNodeId;

      var parentId = namespaceNodeId;
      if (discovered.ClassName is not null)
      {
        var classNodeId = NodeIdBuilder.Class(namespaceNodeId, discovered.ClassName);
        if (emittedClasses.Add(classNodeId))
        {
          var classNode = new TestNode(
                  Id: classNodeId,
                  DisplayName: discovered.ClassName,
                  ParentId: namespaceNodeId,
                  FilePath: discovered.FilePath,
                  SignatureLine: null,
                  BodyStartLine: null,
                  EndLine: null,
                  Type: new NodeType.TestClass(),
                  ProjectId: projectNodeId,
                  AvailableActions: [TestAction.Run, TestAction.Debug]
              );
          registry.Register(classNode);
          await dispatcher.SendRegisterTestAsync(classNode);
        }
        parentId = classNodeId;
      }

      var shortMethodName = discovered.MethodName.Contains('.')
          ? discovered.MethodName[(discovered.MethodName.LastIndexOf('.') + 1)..]
          : discovered.MethodName;

      var shortName = discovered.Arguments is not null
          ? $"{shortMethodName}({discovered.Arguments})"
          : shortMethodName;

      var location = locator.Locate(discovered.FilePath, shortMethodName);
      var methodNodeId = NodeIdBuilder.Method(parentId, discovered.MethodName);
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
      await dispatcher.SendRegisterTestAsync(methodNode);

    }, token.Ct);

    await dispatcher.SendStatusAsync(projectNodeId, new TestNodeStatus.Idle());
  }

  private static IReadOnlyList<string> StripRootNamespace(
         IReadOnlyList<string> parts,
         string[] rootParts)
  {
    if (parts.Count < rootParts.Length)
    {
      return parts;
    }


    for (var i = 0; i < rootParts.Length; i++)
    {
      if (!string.Equals(parts[i], rootParts[i], StringComparison.OrdinalIgnoreCase))
      {

        return parts;
      }

    }

    return [.. parts.Skip(rootParts.Length)];
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
    {
      leafNodes.Add(self);
    }


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
    {
      await adapter.DebugAsync(project, nativeIds, onResult, token.Ct);
    }
    else
    {
      await adapter.RunAsync(project, nativeIds, onResult, token.Ct);
    }

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
       HashSet<string> emitted)
  {
    var currentParentId = projectNodeId;
    var skipCount = originalParts.Count - displayParts.Count;

    for (var i = 0; i < originalParts.Count; i++)
    {
      var segmentParts = originalParts.Take(i + 1).ToArray();
      var nsId = NodeIdBuilder.Namespace(projectNodeId, segmentParts);

      if (emitted.Add(nsId) && i >= skipCount)
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
        await dispatcher.SendRegisterTestAsync(nsNode);
      }

      if (i >= skipCount)
      {
        currentParentId = nsId;
      }

    }

    return currentParentId;
  }

  private static (TestNodeStatus status, List<TestAction> actions) BuildStatusAndActions(TestRunResult result)
  {
    var baseActions = new List<TestAction> { TestAction.Run, TestAction.Debug, TestAction.GoToSource };
    var hasDetails = result.Frames.Length > 0 || result.Stdout.Length > 0 || result.ErrorMessage.Length > 0;
    if (hasDetails)
    {
      baseActions.Add(TestAction.PeekResults);
    }


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
          .Where(n => runningLeafIds.Contains(n.Id))
          .ToList();

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

  private static string FormatDuration(long ms) =>
      ms switch
      {
        >= 60_000 => $"{ms / 60_000.0:F1} m",
        >= 1_000 => $"{ms / 1_000.0:F1} s",
        _ => $"{ms} ms"
      };
}