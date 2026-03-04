using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.TestRunner.Adapters;
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
  // -------------------------------------------------------------------------
  // Discovery
  // -------------------------------------------------------------------------

  public async Task DiscoverProjectAsync(ValidatedDotnetProject project, string solutionNodeId, OperationToken token)
  {
    var projectNodeId = EnsureProjectNode(project, solutionNodeId);
    await dispatcher.SendStatusAsync(projectNodeId, new TestNodeStatus.Discovering());

    // Wipe stale child nodes from a previous discovery pass
    registry.ClearDescendants(projectNodeId);

    var adapter = adapterResolver.Resolve(project);
    var emittedNamespaces = new HashSet<string>();
    var emittedClasses = new HashSet<string>();

    await adapter.DiscoverAsync(project.TargetPath!, async discovered =>
    {
      token.Ct.ThrowIfCancellationRequested();

      // 1. Namespace chain
      var namespaceNodeId = await EnsureNamespaceChainAsync(
              projectNodeId, project.ProjectFullPath, discovered.NamespaceParts,
              emittedNamespaces, token.Ct);

      // 2. TestClass node (if applicable)
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
                  LineNumber: null,
                  Type: new NodeType.TestClass(),
                  ProjectId: projectNodeId,
                  AvailableActions: [TestAction.Run, TestAction.Debug]
              );
          registry.Register(classNode);
          await dispatcher.SendRegisterTestAsync(classNode);
        }
        parentId = classNodeId;
      }

      // 3. TestMethod / Subcase node
      var methodNodeId = NodeIdBuilder.Method(parentId, discovered.MethodName);
      var methodNode = new TestNode(
              Id: methodNodeId,
              DisplayName: discovered.DisplayName,
              ParentId: parentId,
              FilePath: discovered.FilePath,
              LineNumber: discovered.LineNumber,
              Type: discovered.Arguments is not null
                  ? new NodeType.Subcase()
                  : new NodeType.TestMethod(),
              ProjectId: projectNodeId,
              AvailableActions: [TestAction.Run, TestAction.Debug, TestAction.GoToSource]
          );
      registry.Register(methodNode, nativeId: discovered.NativeId);
      await dispatcher.SendRegisterTestAsync(methodNode);

    }, token.Ct);

    await dispatcher.SendStatusAsync(projectNodeId, new TestNodeStatus.Idle());
  }

  // -------------------------------------------------------------------------
  // Run / Debug
  // -------------------------------------------------------------------------

  public async Task RunNodeAsync(string nodeId, ValidatedDotnetProject project, OperationToken token, bool debug = false)
  {
    // Reset detail cache for the whole subtree before running
    detailStore.ClearSubtree(
        registry.GetDescendants(nodeId).Select(n => n.Id).Append(nodeId));

    // Reset statuses to null (signals new operation to client)
    await dispatcher.SendToSubtreeAsync(nodeId, null, registry);
    await dispatcher.SendStatusAsync(nodeId, debug ? new TestNodeStatus.Debugging() : new TestNodeStatus.Running());

    var leafNodes = registry.GetLeafDescendants(nodeId).ToList();
    var nativeIds = leafNodes
        .Select(n => registry.GetNativeId(n.Id))
        .Where(id => id is not null)
        .Select(id => id!)
        .ToList();

    if (nativeIds.Count == 0)
    {
      logger.LogWarning("No runnable tests found under node {NodeId}", nodeId);
      return;
    }

    var adapter = adapterResolver.Resolve(project);

    Func<TestRunResult, Task> onResult = async result =>
    {
      var stableId = registry.GetStableId(result.NativeId);
      if (stableId is null)
      {
        logger.LogWarning("Received result for unknown native ID {NativeId}", result.NativeId);
        return;
      }

      // Cache details — client fetches on demand
      detailStore.Set(stableId, new TestDetail(
              ErrorMessage: result.ErrorMessage,
              DurationMs: result.DurationMs,
              Frames: result.Frames,
              FailingFrame: result.FailingFrame,
              Stdout: result.Stdout
          ));

      var (status, actions) = BuildStatusAndActions(result);
      await dispatcher.SendStatusAsync(stableId, status, actions);
      await BubbleStatusAsync(stableId);
    };

    if (debug)
      await adapter.DebugAsync(project.TargetPath!, nativeIds, onResult, token.Ct);
    else
      await adapter.RunAsync(project.TargetPath!, nativeIds, onResult, token.Ct);
  }

  // -------------------------------------------------------------------------
  // Helpers
  // -------------------------------------------------------------------------

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
          LineNumber: null,
          Type: new NodeType.Project(),
          ProjectId: projectNodeId,
          AvailableActions: [TestAction.Run, TestAction.Debug, TestAction.Invalidate]
      );
      registry.Register(node);
      // Fire-and-forget notification; the caller awaits the full discover
      _ = dispatcher.SendRegisterTestAsync(node);
    }

    return projectNodeId;
  }

  private async Task<string> EnsureNamespaceChainAsync(
      string projectNodeId,
      string projectPath,
      IReadOnlyList<string> parts,
      HashSet<string> emitted,
      CancellationToken ct)
  {
    var currentParentId = projectNodeId;

    for (var i = 0; i < parts.Count; i++)
    {
      var segmentParts = parts.Take(i + 1).ToArray();
      var nsId = NodeIdBuilder.Namespace(projectNodeId, segmentParts);

      if (emitted.Add(nsId))
      {
        var nsNode = new TestNode(
            Id: nsId,
            DisplayName: parts[i],
            ParentId: currentParentId,
            FilePath: null,
            LineNumber: null,
            Type: new NodeType.Namespace(),
            ProjectId: projectNodeId,
            AvailableActions: [TestAction.Run, TestAction.Debug]
        );
        registry.Register(nsNode);
        await dispatcher.SendRegisterTestAsync(nsNode);
      }

      currentParentId = nsId;
    }

    return currentParentId;
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

  /// <summary>
  /// Walks up from a leaf node, recalculating aggregate status for each ancestor.
  /// Worst-child wins: Failed > Skipped > Passed.
  /// </summary>
  private async Task BubbleStatusAsync(string leafId)
  {
    var node = registry.Get(leafId);
    var parentId = node?.ParentId;

    while (parentId is not null)
    {
      var children = registry.GetChildren(parentId).ToList();
      if (children.Count == 0) break;

      // Aggregate: any failed child → parent is failed, etc.
      // We check DetailStore for leaf nodes; for parent nodes we rely on previously
      // bubbled statuses already stored in DetailStore at aggregate level.
      // For now emit Running on the parent during an in-progress run, and the
      // final aggregate after all results are in.
      // TODO: track per-parent aggregate properly once run completes

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