using System.Collections.Generic;
using System.Linq;

using EasyDotnet.Types;

using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.VSTest;

public static class DiscoverHandler
{
  public static Dictionary<string, List<DiscoveredTest>> Discover(string vsTestConsolePath, string[] testDllPath)
  {
    var options = new TestPlatformOptions
    {
      CollectMetrics = false,
      SkipDefaultAdapters = false
    };

    var r = new VsTestConsoleWrapper(vsTestConsolePath);
    var sessionHandler = new TestSessionHandler();
    var discoveryHandler = new PlaygroundTestDiscoveryHandler();
    r.DiscoverTests(testDllPath, null, options, sessionHandler.TestSessionInfo, discoveryHandler);

    return discoveryHandler.TestCases.GroupBy(x => x.Source).ToDictionary(x => x.Key, y => y.Select(x => x.ToDiscoveredTest()).ToList());
  }
}

public class PlaygroundTestDiscoveryHandler() : ITestDiscoveryEventsHandler, ITestDiscoveryEventsHandler2
{
  public List<TestCase> TestCases { get; internal set; } = [];

  public void HandleDiscoveredTests(IEnumerable<TestCase> discoveredTestCases)
  {
    if (discoveredTestCases != null) { TestCases.AddRange(discoveredTestCases); }
  }

  public void HandleDiscoveryComplete(long totalTests, IEnumerable<TestCase> lastChunk, bool isAborted)
  {
    if (lastChunk != null) { TestCases.AddRange(lastChunk); }
  }

  public void HandleDiscoveryComplete(DiscoveryCompleteEventArgs discoveryCompleteEventArgs, IEnumerable<TestCase> lastChunk)
  {
    if (lastChunk != null) { TestCases.AddRange(lastChunk); }
  }

  public void HandleLogMessage(TestMessageLevel level, string message) { }
  public void HandleRawMessage(string rawMessage) { }
}