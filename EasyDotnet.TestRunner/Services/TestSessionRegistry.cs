using System.Collections.Concurrent;
using EasyDotnet.Domain.Models.Test;
using EasyDotnet.TestRunner.Abstractions;
using EasyDotnet.TestRunner.Models;
using EasyDotnet.TestRunner.Notifications;
using StreamJsonRpc;

namespace EasyDotnet.TestRunner.Services;

public class TestSessionRegistry(JsonRpc jsonRpc) : ITestSessionRegistry
{
  private readonly ConcurrentDictionary<string, TestNode> _nodes = new();
  private readonly ConcurrentDictionary<string, TestNodeStatus> _statuses = new();
  private readonly ConcurrentDictionary<string, TestRunResult> _results = new();
  private readonly object _stateLock = new();
  private bool _isLoading;

  public bool IsLoading
  {
    get { lock (_stateLock) return _isLoading; }
  }

  public IDisposable AcquireLock()
  {
    lock (_stateLock)
    {
      if (_isLoading)
      {
        throw new InvalidOperationException("Operation already in progress.");
      }

      _isLoading = true;
      BroadcastGlobalStatus();
    }

    return new RunnerLock(this);
  }

  private void ReleaseLock()
  {
    lock (_stateLock)
    {
      _isLoading = false;
      BroadcastGlobalStatus();
    }
  }

  public IEnumerable<TestNode> GetChildren(string parentId) => _nodes.Values.Where(n => n.ParentId == parentId);

  public IEnumerable<TestNode> GetDescendants(string parentId)
  {
    foreach (var child in GetChildren(parentId))
    {
      yield return child;

      foreach (var descendant in GetDescendants(child.Id))
      {
        yield return descendant;
      }
    }
  }

  public void RegisterNode(TestNode node)
  {
    _nodes[node.Id] = node;
    _statuses.TryAdd(node.Id, new TestNodeStatus.Idle());
    _ = jsonRpc.NotifyWithParameterObjectAsync("registerTest", node);
  }

  public void RegisterTestResult(TestRunResult result)
  {
    _results[result.Id] = result;
    var status = MapToStatus(result);
    UpdateStatus(result.Id, status);
  }

  public TestRunResult? GetTestResult(string nodeId) => _results.TryGetValue(nodeId, out var result) ? result : null;

  public void UpdateStatus(string nodeId, TestNodeStatus newStatus)
  {
    if (!_statuses.TryGetValue(nodeId, out var _)) return;

    _statuses[nodeId] = newStatus;

    var withActions = CalculateStatusWithActions(nodeId, newStatus);
    _ = jsonRpc.NotifyWithParameterObjectAsync("updateStatus", new TestNodeStatusUpdateNotification(nodeId, withActions));

    BroadcastGlobalStatus();
  }

  public bool Contains(string nodeId) => _nodes.ContainsKey(nodeId);

  public bool TryGetNode(string nodeId, out TestNode? node) => _nodes.TryGetValue(nodeId, out node);

  public void UpdateNodeDisplayName(string nodeId, string newDisplayName)
  {
    if (_nodes.TryGetValue(nodeId, out var node))
    {
      var updated = node with { DisplayName = newDisplayName };
      _nodes[nodeId] = updated;
      _ = jsonRpc.NotifyWithParameterObjectAsync("registerTest", updated);
    }
  }

  public TestNode? GetNode(string id) => _nodes.TryGetValue(id, out var node) ? node : null;
  public IEnumerable<TestNode> GetAllNodes() => _nodes.Values;

  /// <summary>
  /// Updates the ParentId of a node and notifies the client to move it in the tree.
  /// </summary>
  public void UpdateNodeParent(string nodeId, string newParentId)
  {
    if (_nodes.TryGetValue(nodeId, out var node))
    {
      _nodes[nodeId] = (node with { ParentId = newParentId });

      _ = jsonRpc.NotifyWithParameterObjectAsync("changeParent", new
      {
        targetId = nodeId,
        newParentId
      });
    }
  }

  /// <summary>
  /// Removes a node from the registry and notifies the client to delete it.
  /// </summary>
  public void RemoveNode(string nodeId)
  {
    if (_nodes.TryRemove(nodeId, out _))
    {
      if (_statuses.TryRemove(nodeId, out _))
      {
        BroadcastGlobalStatus();
      }

      _ = jsonRpc.NotifyWithParameterObjectAsync("removeNode", new { nodeId });
    }
  }

  private void BroadcastGlobalStatus()
  {
    var values = _statuses.Values;
    var passed = values.Count(x => x is TestNodeStatus.Passed);
    var failed = values.Count(x => x is TestNodeStatus.Failed);
    var skipped = values.Count(x => x is TestNodeStatus.Skipped);

    var overall = OverallStatusEnum.Passed;
    if (failed > 0) overall = OverallStatusEnum.Failed;
    else if (_isLoading) overall = OverallStatusEnum.Running; // Optional: Or keep previous state
    else if (passed == 0 && failed == 0) overall = OverallStatusEnum.Idle;

    var status = new TestRunnerStatus(
        IsLoading: _isLoading,
        OverallStatus: overall,
        TotalPassed: passed,
        TotalFailed: failed,
        TotalSkipped: skipped
    );

    _ = jsonRpc.NotifyWithParameterObjectAsync("statusChanged", status);
  }

  private TestNodeStatus CalculateStatusWithActions(string nodeId, TestNodeStatus rawStatus)
  {
    var node = GetNode(nodeId);
    if (node == null) return rawStatus;

    var actions = new List<TestAction>
    {
      TestAction.Run
    };

    if (node.Type is NodeType.TestMethod or NodeType.Subcase)
    {
      actions.Add(TestAction.Debug);
    }

    if (!string.IsNullOrEmpty(node.FilePath))
    {
      actions.Add(TestAction.GoToSource);
    }

    if (rawStatus is TestNodeStatus.Failed or TestNodeStatus.Passed)
    {
      actions.Add(TestAction.PeekOutput);
    }

    if (node.Type is NodeType.Project or NodeType.Solution)
    {
      actions.Add(TestAction.Refresh);
    }

    return rawStatus with { Actions = actions };
  }

  private static TestNodeStatus MapToStatus(TestRunResult result)
  {
    var ms = result.Duration ?? 0;
    var durationDisplay = ms < 1000 ? $"{ms}ms" : $"{ms / 1000d:0.##}s";

    return result.Outcome?.ToLowerInvariant() switch
    {
      "passed" => new TestNodeStatus.Passed(durationDisplay),
      "failed" => new TestNodeStatus.Failed(durationDisplay, string.Join("\n", result.ErrorMessage ?? [])),
      "skipped" => new TestNodeStatus.Skipped("Skipped"),
      _ => new TestNodeStatus.Skipped(result.Outcome ?? "Unknown")
    };
  }

  private sealed class RunnerLock(TestSessionRegistry registry) : IDisposable
  {
    private bool _disposed;

    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      registry.ReleaseLock();
    }
  }
}