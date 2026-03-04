using EasyDotnet.IDE.TestRunner.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.IDE.TestRunner.Adapters;

/// <summary>
/// VSTest discovery handler that invokes a callback per test as they arrive,
/// enabling streaming registerTest notifications rather than end-of-batch delivery.
/// </summary>
internal sealed class StreamingDiscoveryHandler(
    Func<DiscoveredTest, Task> onDiscovered,
    ILoggerFactory loggerFactory) : ITestDiscoveryEventsHandler2
{
  private readonly ILogger _logger = loggerFactory.CreateLogger<StreamingDiscoveryHandler>();
  private readonly TaskCompletionSource _tcs = new();

  public Task Completion => _tcs.Task;

  public void HandleDiscoveredTests(IEnumerable<TestCase>? discoveredTestCases)
  {
    if (discoveredTestCases is null) return;
    foreach (var testCase in discoveredTestCases)
    {
      // Fire-and-forget per test: callback handles its own async work
      // (emitting registerTest notification). Errors are logged, not thrown.
      _ = Task.Run(async () =>
      {
        try { await onDiscovered(testCase.ToDiscoveredTest()); }
        catch (Exception ex) { _logger.LogError(ex, "Error in onDiscovered callback for {TestCase}", testCase.FullyQualifiedName); }
      });
    }
  }

  public void HandleDiscoveryComplete(DiscoveryCompleteEventArgs discoveryCompleteEventArgs, IEnumerable<TestCase>? lastChunk)
  {
    if (lastChunk is not null) HandleDiscoveredTests(lastChunk);
    _tcs.TrySetResult();
  }

  public void HandleLogMessage(TestMessageLevel level, string? message)
  {
    if (message is not null)
      _logger.LogDebug("[VSTest Discovery] {Level}: {Message}", level, message);
  }

  public void HandleRawMessage(string rawMessage) { }
}