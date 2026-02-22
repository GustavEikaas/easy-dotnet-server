using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.IDE.VSTest;

public class TestDiscoveryHandler(ILogger<TestDiscoveryHandler> logger) : ITestDiscoveryEventsHandler, ITestDiscoveryEventsHandler2
{
  public List<TestCase> TestCases { get; internal set; } = [];

  public void HandleDiscoveredTests(IEnumerable<TestCase>? discoveredTestCases)
  {
    if (discoveredTestCases == null)
    {
      return;
    }

    logger.LogInformation("HandleDiscoveredTests called with {count} test cases.", discoveredTestCases.Count());

    foreach (var tc in discoveredTestCases)
    {
      logger.LogInformation("[+] Discovered test: {name} ({id}) in {source}", tc.DisplayName, tc.Id, tc.Source);
      TestCases.Add(tc);
    }
  }

  public void HandleDiscoveryComplete(long totalTests, IEnumerable<TestCase>? lastChunk, bool isAborted)
  {
    logger.LogInformation("HandleDiscoveryComplete(long,int,bool): total={totalTests}, isAborted={isAborted}", totalTests, isAborted);

    if (lastChunk != null)
    {
      var list = lastChunk.ToList();
      logger.LogInformation("HandleDiscoveryComplete received final chunk of {count} tests.", list.Count);

      foreach (var tc in list)
      {
        logger.LogInformation("[+] Final chunk test: {name} ({id}) in {source}", tc.DisplayName, tc.Id, tc.Source);
      }

      TestCases.AddRange(list);
    }
  }

  public void HandleDiscoveryComplete(DiscoveryCompleteEventArgs args, IEnumerable<TestCase>? lastChunk)
  {
    logger.LogInformation("HandleDiscoveryComplete(DiscoveryCompleteEventArgs): total={totalTests}, aborted={aborted}", args.TotalCount, args.IsAborted);

    if (lastChunk != null)
    {
      var list = lastChunk.ToList();

      logger.LogInformation("HandleDiscoveryComplete2 final chunk size: {count}", list.Count);

      foreach (var tc in list)
      {
        logger.LogInformation("[+] Final chunk test: {name} ({id}) in {source}", tc.DisplayName, tc.Id, tc.Source);
      }

      TestCases.AddRange(list);
    }
  }

  public void HandleLogMessage(TestMessageLevel level, string? message)
  {
    switch (level)
    {
      case TestMessageLevel.Informational:
        logger.LogInformation("[VSTest] {Message}", message);
        break;

      case TestMessageLevel.Warning:
        logger.LogWarning("[VSTest] {Message}", message);
        break;

      case TestMessageLevel.Error:
        logger.LogError("[VSTest] {Message}", message);
        break;

      default:
        logger.LogInformation("[VSTest:Unknown] {Message}", message);
        break;
    }
  }

  public void HandleRawMessage(string rawMessage) =>
    logger.LogDebug("[VSTest:Raw] {RawMessage}", rawMessage);
}