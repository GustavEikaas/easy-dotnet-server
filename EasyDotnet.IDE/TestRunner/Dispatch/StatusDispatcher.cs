using EasyDotnet.Domain.Models.MsBuild.Build;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;
using StreamJsonRpc;

namespace EasyDotnet.IDE.TestRunner.Dispatch;

/// <summary>
/// Single choke point for all outbound JSON-RPC notifications to the Lua client.
/// Nothing else touches the JsonRpc layer directly.
/// </summary>
public class StatusDispatcher(JsonRpc rpc, NodeRegistry registry)
{
  /// <summary>Notify the client of a node registration or update.</summary>
  public Task SendRegisterTestAsync(TestNode node)
  {
    ArgumentNullException.ThrowIfNull(node);
    return rpc.NotifyWithParameterObjectAsync("registerTest", new { test = node });
  }


  /// <summary>
  /// Send a status update for a single node, optionally with updated available actions.
  /// Passing null status signals a reset to Idle (new operation starting).
  /// </summary>
  public Task SendStatusAsync(string nodeId, TestNodeStatus? status, List<TestAction>? availableActions = null)
  {
    if (!registry.SetLastStatus(nodeId, status))
    {
      return Task.CompletedTask;
    }

    return rpc.NotifyWithParameterObjectAsync("updateStatus", new
    {
      id = nodeId,
      status,
      availableActions
    });
  }


  /// <summary>Broadcast global runner status (IsLoading, aggregate counts, etc.).</summary>
  public Task SendRunnerStatusAsync(TestRunnerStatus status) =>
      rpc.NotifyWithParameterObjectAsync("testrunner/statusUpdate", status);

  public Task SendBuildErrorsAsync(string projectNodeId, IEnumerable<BuildMessageWithProject> errors) =>
          rpc.NotifyWithParameterObjectAsync("testrunner/buildErrors", new
          {
            projectNodeId,
            errors
          });

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
}