using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.IDE.TestRunner.Adapters;

/// <summary>
/// VSTest discovery handler that exposes raw <see cref="TestCase"/>s through an asynchronous
/// enumerable sequence as they arrive. Conversion to <see cref="Models.DiscoveredTest"/>
/// happens downstream in <see cref="VsTestAdapter"/> so duplicate-FQN batching (the only
/// reliable VSTest-layer signal for parameterised rows) can be applied.
/// </summary>
internal sealed class StreamingDiscoveryHandler(ILoggerFactory loggerFactory) : ITestDiscoveryEventsHandler2
{
  private readonly ILogger _logger = loggerFactory.CreateLogger<StreamingDiscoveryHandler>();
  private readonly Channel<TestCase> _channel = Channel.CreateUnbounded<TestCase>();
  public void HandleDiscoveredTests(IEnumerable<TestCase>? discoveredTestCases)
  {
    if (discoveredTestCases is null) return;
    foreach (var testCase in discoveredTestCases)
    {
      if (!_channel.Writer.TryWrite(testCase))
      {
        _logger.LogWarning("Failed to write test case {TestCase} to the channel", testCase.FullyQualifiedName);
      }
    }
  }
  public IAsyncEnumerable<TestCase> ReadAllAsync()
  {
    return _channel.Reader.ReadAllAsync();
  }
  public void HandleDiscoveryComplete(DiscoveryCompleteEventArgs discoveryCompleteEventArgs, IEnumerable<TestCase>? lastChunk)
  {
    if (lastChunk is not null) HandleDiscoveredTests(lastChunk);
    _channel.Writer.Complete();
  }

  public void HandleLogMessage(TestMessageLevel level, string? message)
  {
    if (message is not null)
      _logger.LogDebug("[VSTest Discovery] {Level}: {Message}", level, message);
  }

  public void HandleRawMessage(string rawMessage) { }
}