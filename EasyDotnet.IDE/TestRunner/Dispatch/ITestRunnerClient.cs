using EasyDotnet.IDE.TestRunner.Models;

namespace EasyDotnet.IDE.TestRunner.Dispatch;

/// <summary>
/// Outbound transport for testrunner events. <see cref="StatusDispatcher"/> owns the
/// filtering (stale operations, unchanged statuses); implementations only deliver.
/// </summary>
public interface ITestRunnerClient
{
  Task RegisterTestAsync(TestNode node);
  Task RemoveTestAsync(string nodeId);
  Task UpdateStatusAsync(string nodeId, TestNodeStatus? status, List<TestAction>? availableActions);
  Task UpdateStatusBatchAsync(IReadOnlyList<StatusUpdate> updates);
  Task SendRunnerStatusAsync(TestRunnerStatus status);
  Task<bool> IsTestRunnerVisibleAsync(CancellationToken ct);
}