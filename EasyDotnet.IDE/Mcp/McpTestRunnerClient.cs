using EasyDotnet.IDE.TestRunner.Dispatch;
using EasyDotnet.IDE.TestRunner.Models;
using ModelContextProtocol;

namespace EasyDotnet.IDE.Mcp;

/// <summary>
/// Testrunner events for the MCP host. Node events have no listener; runner status is forwarded
/// as MCP progress to whichever tool call is currently running a test operation.
/// </summary>
public sealed class McpTestRunnerClient : ITestRunnerClient
{
  private IProgress<ProgressNotificationValue>? _progress;

  public IDisposable ReportTo(IProgress<ProgressNotificationValue> progress)
  {
    _progress = progress;
    return new Reset(this);
  }

  public Task RegisterTestAsync(TestNode node) => Task.CompletedTask;
  public Task RemoveTestAsync(string nodeId) => Task.CompletedTask;
  public Task UpdateStatusAsync(string nodeId, TestNodeStatus? status, List<TestAction>? availableActions) => Task.CompletedTask;
  public Task UpdateStatusBatchAsync(IReadOnlyList<StatusUpdate> updates) => Task.CompletedTask;

  public Task SendRunnerStatusAsync(TestRunnerStatus status)
  {
    if (_progress is { } progress && status.IsLoading)
    {
      var done = status.TotalPassed + status.TotalFailed + status.TotalSkipped + status.TotalCancelled + status.TotalInconclusive;
      progress.Report(new ProgressNotificationValue
      {
        Progress = done,
        Total = status.TotalTests > 0 ? status.TotalTests : null,
        Message = status.CurrentOperation ?? status.OverallStatus.ToString(),
      });
    }
    return Task.CompletedTask;
  }

  // Nobody is watching a test runner panel, so never raise an OS notification.
  public Task<bool> IsTestRunnerVisibleAsync(CancellationToken ct) => Task.FromResult(true);

  private sealed class Reset(McpTestRunnerClient owner) : IDisposable
  {
    public void Dispose() => owner._progress = null;
  }
}