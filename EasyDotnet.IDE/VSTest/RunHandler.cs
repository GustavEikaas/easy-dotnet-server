using System;
using System.Collections.Generic;
using System.Linq;

using EasyDotnet.Types;

using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.VSTest;

public static class RunHandler
{
  public static List<TestRunResult> RunTests(string vsTestPath, string dllPath, Guid[] testIds)
  {
    var options = new TestPlatformOptions
    {
      CollectMetrics = false,
      SkipDefaultAdapters = false
    };

    var discoveryHandler = new PlaygroundTestDiscoveryHandler();
    var testHost = new VsTestConsoleWrapper(vsTestPath);
    var sessionHandler = new TestSessionHandler();
    var handler = new TestRunHandler();

    //TODO: Caching mechanism to prevent rediscovery on each run request.
    //Alternative check for overloads of RunTests that support both dllPath and testIds
    testHost.DiscoverTests([dllPath], null, options, sessionHandler.TestSessionInfo, discoveryHandler);
    var runTests = discoveryHandler.TestCases.Where(x => testIds.Contains(x.Id));
    testHost.RunTests(runTests, null, options, sessionHandler.TestSessionInfo, handler);

    return [.. handler.Results.Select(x => x.ToTestRunResult())];
  }

  internal sealed class TestRunHandler() : ITestRunEventsHandler
  {
    public List<TestResult> Results = [];

    public void HandleLogMessage(TestMessageLevel level, string? message) { }

    public void HandleRawMessage(string rawMessage) { }

    public void HandleTestRunComplete(TestRunCompleteEventArgs testRunCompleteArgs, TestRunChangedEventArgs? lastChunkArgs, ICollection<AttachmentSet>? runContextAttachments, ICollection<string>? executorUris) { }
    public void HandleTestRunStatsChange(TestRunChangedEventArgs? testRunChangedArgs)
    {
      if (testRunChangedArgs?.NewTestResults is not null)
      {
        Results.AddRange(testRunChangedArgs.NewTestResults);
      }
    }

    public int LaunchProcessWithDebuggerAttached(TestProcessStartInfo testProcessStartInfo) => throw new NotImplementedException();
  }
}