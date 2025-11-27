using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Test;
using Microsoft.Extensions.Logging;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.Services;


public class VsTestService(IMsBuildService msBuildService, ILogger<VsTestService> logService) : IVsTestService
{
  public List<DiscoveredTest> RunDiscover(string dllPath)
  {
    var vsTestPath = GetVsTestPath();
    logService.LogInformation("Using VSTest path: {vsTestPath}", vsTestPath);
    var discoveredTests = Discover(vsTestPath, [dllPath]);
    discoveredTests.TryGetValue(Path.GetFileName(dllPath), out var tests);
    return tests ?? [];
  }

  public List<TestRunResult> RunTests(string dllPath, Guid[] testIds)
  {
    var vsTestPath = GetVsTestPath();
    logService.LogInformation("Using VSTest path: {vsTestPath}", vsTestPath);
    var testResults = RunTests(vsTestPath, dllPath, testIds);
    return testResults;
  }

  private string GetVsTestPath()
  {
    var sdk = msBuildService.QuerySdkInstallations();
    return Path.Join(sdk.ToList()[0].MSBuildPath, "vstest.console.dll");
  }

  private Dictionary<string, List<DiscoveredTest>> Discover(string vsTestConsolePath, string[] testDllPath)
  {
    var options = new TestPlatformOptions
    {
      CollectMetrics = false,
      SkipDefaultAdapters = false
    };

    var runner = new VsTestConsoleWrapper(vsTestConsolePath);
    var sessionHandler = new TestSessionHandler();
    var discoveryHandler = new PlaygroundTestDiscoveryHandler(logService);
    runner.DiscoverTests(testDllPath, null, options, sessionHandler.TestSessionInfo, discoveryHandler);

    return discoveryHandler.TestCases.GroupBy(x => Path.GetFileName(x.Source)).ToDictionary(x => x.Key, y => y.Select(x => x.ToDiscoveredTest()).ToList());
  }

  private List<TestRunResult> RunTests(string vsTestPath, string dllPath, Guid[] testIds)
  {
    var options = new TestPlatformOptions
    {
      CollectMetrics = false,
      SkipDefaultAdapters = false
    };

    var discoveryHandler = new PlaygroundTestDiscoveryHandler(logService);
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
}

public class PlaygroundTestDiscoveryHandler(ILogger<VsTestService> logger) : ITestDiscoveryEventsHandler, ITestDiscoveryEventsHandler2
{
  public List<TestCase> TestCases { get; internal set; } = [];

  public void HandleDiscoveredTests(IEnumerable<TestCase>? discoveredTestCases)
  {
    if (discoveredTestCases == null)
      return;


    logger.LogInformation("HandleDiscoveredTests called with {count} test cases.", discoveredTestCases.Count());

    foreach (var tc in discoveredTestCases)
    {
      logger.LogInformation(
          "  [+] Discovered test: {name} ({id}) in {source}",
          tc.DisplayName, tc.Id, tc.Source);
      TestCases.Add(tc);
    }

  }

  public void HandleDiscoveryComplete(long totalTests, IEnumerable<TestCase>? lastChunk, bool isAborted)
  {
    logger.LogInformation(
        "HandleDiscoveryComplete(long,int,bool): total={totalTests}, isAborted={isAborted}",
        totalTests, isAborted);

    if (lastChunk != null)
    {
      var list = lastChunk.ToList();
      logger.LogInformation("HandleDiscoveryComplete received final chunk of {count} tests.", list.Count);

      foreach (var tc in list)
      {
        logger.LogInformation(
            "  [+] Final chunk test: {name} ({id}) in {source}",
            tc.DisplayName, tc.Id, tc.Source);
      }

      TestCases.AddRange(list);
    }
  }

  public void HandleDiscoveryComplete(DiscoveryCompleteEventArgs args, IEnumerable<TestCase>? lastChunk)
  {
    logger.LogInformation(
        "HandleDiscoveryComplete(DiscoveryCompleteEventArgs): total={totalTests}, aborted={aborted}",
        args.TotalCount, args.IsAborted);

    if (lastChunk != null)
    {
      var list = lastChunk.ToList();

      logger.LogInformation("HandleDiscoveryComplete2 final chunk size: {count}", list.Count);

      foreach (var tc in list)
      {
        logger.LogInformation(
            "  [+] Final chunk test: {name} ({id}) in {source}",
            tc.DisplayName, tc.Id, tc.Source);
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


internal sealed class TestRunHandler() : ITestRunEventsHandler
{
  public List<Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult> Results = [];


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


internal class TestSessionHandler : ITestSessionEventsHandler
{
  public TestSessionInfo? TestSessionInfo { get; private set; }

  public void HandleLogMessage(TestMessageLevel level, string? message) => throw new NotImplementedException();

  public void HandleRawMessage(string rawMessage) { }
  public void HandleStartTestSessionComplete(StartTestSessionCompleteEventArgs? eventArgs) => TestSessionInfo = eventArgs?.TestSessionInfo;
  public void HandleStopTestSessionComplete(StopTestSessionCompleteEventArgs? eventArgs) { }
}


public static class TestCaseExtensions
{
  public static DiscoveredTest ToDiscoveredTest(this TestCase x)
  {
    var name = x.DisplayName.Contains('.') ? x.DisplayName : x.FullyQualifiedName;
    return new()
    {
      Id = x.Id.ToString(),
      Namespace = x.FullyQualifiedName,
      Name = name,
      FilePath = x.CodeFilePath?.Replace("\\", "/"),
      LineNumber = x.LineNumber,
      DisplayName = x.DisplayName
    };
  }


  public static TestRunResult ToTestRunResult(this Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult x) => new()
  {
    Duration = (long?)x.Duration.TotalMilliseconds,
    StackTrace = (x.ErrorStackTrace?.Split(Environment.NewLine) ?? []),
    ErrorMessage = x.ErrorMessage?.Split(Environment.NewLine) ?? [],
    Id = x.TestCase.Id.ToString(),
    Outcome = GetTestOutcome(x.Outcome),
    StdOut = (x.GetStandardOutput()?.Split(Environment.NewLine) ?? [])
  };

  public static string GetTestOutcome(TestOutcome outcome) => outcome switch
  {
    TestOutcome.None => "none",
    TestOutcome.Passed => "passed",
    TestOutcome.Failed => "failed",
    TestOutcome.Skipped => "skipped",
    TestOutcome.NotFound => "not found",
    _ => "",
  };

  private static string? GetStandardOutput(this Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult testResult)
    => testResult.Messages.FirstOrDefault(message => message.Category == TestResultMessage.StandardOutCategory)?.Text;
}