using EasyDotnet.Domain.Models.MsBuild.Build;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;
using StreamJsonRpc;

namespace EasyDotnet.IDE.TestRunner.Dispatch;

/// <summary>
/// Single choke point for all outbound JSON-RPC notifications to the Lua client.
/// Nothing else touches the JsonRpc layer directly.
/// </summary>
public class StatusDispatcher(JsonRpc rpc)
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
  public Task SendStatusAsync(string nodeId, TestNodeStatus? status, List<TestAction>? availableActions = null) =>
      rpc.NotifyWithParameterObjectAsync("updateStatus", new
      {
        id = nodeId,
        status,
        availableActions
      });

  /// <summary>Broadcast global runner status (IsLoading, aggregate counts, etc.).</summary>
  public Task SendRunnerStatusAsync(TestRunnerStatus status) =>
      rpc.NotifyWithParameterObjectAsync("testrunner/statusUpdate", status);

  /// <summary>
  /// Sends a status update to a node and all its descendants.
  /// Used for bulk resets at the start of an operation.
  /// </summary>
  public async Task SendToSubtreeAsync(
      string rootId,
      TestNodeStatus? status,
      NodeRegistry registry,
      List<TestAction>? availableActions = null)
  {
    await SendStatusAsync(rootId, status, availableActions);
    foreach (var descendant in registry.GetDescendants(rootId))
      await SendStatusAsync(descendant.Id, status, availableActions);
  }

  public Task SendBuildErrorsAsync(string projectNodeId, IEnumerable<BuildMessageWithProject> errors) =>
          rpc.NotifyWithParameterObjectAsync("testrunner/buildErrors", new
          {
            projectNodeId,
            errors
          });
}