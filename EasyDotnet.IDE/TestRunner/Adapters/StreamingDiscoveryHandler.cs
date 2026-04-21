using System.Threading.Channels;
using EasyDotnet.IDE.TestRunner.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.IDE.TestRunner.Adapters;

/// <summary>
/// VSTest discovery handler that exposes tests through an asynchronous enumerable sequence as they arrive,
/// enabling streaming registerTest notifications rather than end-of-batch delivery.
/// </summary>
internal sealed class StreamingDiscoveryHandler(ILoggerFactory loggerFactory) : ITestDiscoveryEventsHandler2
{
  private readonly ILogger _logger = loggerFactory.CreateLogger<StreamingDiscoveryHandler>();
  private readonly Channel<DiscoveredTest> _channel = Channel.CreateUnbounded<DiscoveredTest>();
  public void HandleDiscoveredTests(IEnumerable<TestCase>? discoveredTestCases)
  {
    if (discoveredTestCases is null) return;
    foreach (var testCase in discoveredTestCases)
    {
      if (!_channel.Writer.TryWrite(testCase.ToDiscoveredTest()))
      {
        _logger.LogWarning("Failed to write test case {TestCase} to the channel", testCase.FullyQualifiedName);
      }
    }
  }
  public IAsyncEnumerable<DiscoveredTest> ReadAllAsync()
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