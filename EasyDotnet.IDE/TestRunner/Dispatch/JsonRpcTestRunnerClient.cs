using EasyDotnet.IDE.TestRunner.Models;
using StreamJsonRpc;

namespace EasyDotnet.IDE.TestRunner.Dispatch;

/// <summary>Delivers testrunner events to the Lua client as JSON-RPC notifications.</summary>
public class JsonRpcTestRunnerClient(JsonRpc rpc) : ITestRunnerClient
{
  public Task RegisterTestAsync(TestNode node) =>
      rpc.NotifyWithParameterObjectAsync("registerTest", new { test = node });

  public Task RemoveTestAsync(string nodeId) =>
      rpc.NotifyWithParameterObjectAsync("removeTest", new { id = nodeId });

  public Task UpdateStatusAsync(string nodeId, TestNodeStatus? status, List<TestAction>? availableActions) =>
      rpc.NotifyWithParameterObjectAsync("updateStatus", new { id = nodeId, status, availableActions });

  public Task UpdateStatusBatchAsync(IReadOnlyList<StatusUpdate> updates) =>
      rpc.NotifyWithParameterObjectAsync("updateStatusBatch", new
      {
        updates = updates.Select(u => new { id = u.Id, status = u.Status, availableActions = u.AvailableActions }).ToList()
      });

  public Task SendRunnerStatusAsync(TestRunnerStatus status) =>
      rpc.NotifyWithParameterObjectAsync("testrunner/statusUpdate", status);

  public Task<bool> IsTestRunnerVisibleAsync(CancellationToken ct) =>
      rpc.InvokeWithParameterObjectAsync<bool>("testrunner/isVisible", new { }, ct);
}