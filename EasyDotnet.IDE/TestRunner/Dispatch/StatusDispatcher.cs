using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;
using StreamJsonRpc;

namespace EasyDotnet.IDE.TestRunner.Dispatch;

/// <summary>
/// Single choke point for all outbound JSON-RPC notifications to the Lua client.
/// Suppresses duplicate status notifications — if a node's status type hasn't changed,
/// the notification is dropped to avoid spamming the client.
/// </summary>
public class StatusDispatcher(JsonRpc rpc, NodeRegistry registry)
{
  /// <summary>Notify the client of a node registration or update.</summary>
  public Task SendRegisterTestAsync(TestNode node) =>
      rpc.NotifyWithParameterObjectAsync("registerTest", new { test = node });

  /// <summary>
  /// Tell the client to remove a node that no longer exists after rediscovery.
  /// Emitted after discovery completes — only for nodes that were not re-registered.
  /// </summary>
  public Task SendRemoveTestAsync(string nodeId) =>
      rpc.NotifyWithParameterObjectAsync("removeTest", new { id = nodeId });

  /// <summary>
  /// Send a status update for a single node.
  /// No-ops if the status type is identical to the last sent status for this node.
  /// </summary>
  public Task SendStatusAsync(string nodeId, TestNodeStatus? status, List<TestAction>? availableActions = null)
  {
    if (!registry.SetLastStatus(nodeId, status))
    {
      return Task.CompletedTask;
    }

    return rpc.NotifyWithParameterObjectAsync("updateStatus", new { id = nodeId, status, availableActions });
  }

  /// <summary>
  /// Sends a single batched status update covering a node and all its descendants.
  /// Filters out nodes whose status type hasn't changed.
  /// </summary>
  public Task SendBatchStatusAsync(
      string rootId,
      TestNodeStatus? status,
      NodeRegistry reg,
      List<TestAction>? availableActions = null)
  {
    var updates = reg.GetDescendants(rootId)
        .Select(n => n.Id)
        .Prepend(rootId)
        .Where(id => reg.SetLastStatus(id, status))
        .Select(id => new { id, status, availableActions })
        .ToList();

    if (updates.Count == 0)
    {
      return Task.CompletedTask;
    }

    return rpc.NotifyWithParameterObjectAsync("updateStatusBatch", new { updates });
  }

  /// <summary>Broadcast global runner status (IsLoading, aggregate counts, etc.).</summary>
  public Task SendRunnerStatusAsync(TestRunnerStatus status) =>
      rpc.NotifyWithParameterObjectAsync("testrunner/statusUpdate", status);
}