using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Test;
using Microsoft.Extensions.Logging;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.Infrastructure.Services;


internal sealed class VsTestOperationQueue
{
  private readonly SemaphoreSlim _lock = new(1, 1);

  public async Task<T> Enqueue<T>(Func<Task<T>> operation)
  {
    await _lock.WaitAsync();
    try
    {
      return await operation();
    }
    finally
    {
      _lock.Release();
    }
  }

  public async Task Enqueue(Func<Task> operation)
  {
    await _lock.WaitAsync();
    try
    {
      await operation();
    }
    finally
    {
      _lock.Release();
    }
  }
}



public class VsTestService(IMsBuildService msBuildService, ILogger<VsTestService> logger) : IVsTestService
{
  private readonly VsTestOperationQueue _queue = new();

  public IAsyncEnumerable<DiscoveredTest> DiscoverAsync(string[] dllPaths, CancellationToken ct)
          => RunQueuedDiscovery(dllPaths, ct);


  private async IAsyncEnumerable<DiscoveredTest> RunQueuedDiscovery(
      string[] dllPaths,
      [EnumeratorCancellation] CancellationToken ct)
  {
    var channel = Channel.CreateUnbounded<DiscoveredTest>();

    var jobCompletion = new TaskCompletionSource();

    await _queue.Enqueue(() =>
    {
      try
      {
        var vsTestPath = GetVsTestPath();
        var wrapper = new VsTestConsoleWrapper(vsTestPath);
        var session = new TestSessionHandler();
        var handler = new StreamedDiscoveryHandler(channel, logger);

        wrapper.DiscoverTests(dllPaths, null, new TestPlatformOptions(), session.TestSessionInfo, handler);
        return Task.CompletedTask;
      }
      catch (Exception ex)
      {
        jobCompletion.SetException(ex);
        throw;
      }
      finally
      {
        channel.Writer.TryComplete();
        jobCompletion.TrySetResult();
      }
    });

    await foreach (var item in channel.Reader.ReadAllAsync(ct))
    {
      yield return item;
    }
  }


  public List<TestRunResult> RunTests(string dllPath, Guid[] testIds)
  {
    var vsTestPath = GetVsTestPath();
    logger.LogInformation("Using VSTest path: {vsTestPath}", vsTestPath);
    throw new NotImplementedException();
    // return testResults;
  }

  private string GetVsTestPath()
  {
    var sdk = msBuildService.QuerySdkInstallations();
    return Path.Join(sdk.ToList()[0].MSBuildPath, "vstest.console.dll");
  }
}

public sealed class StreamedDiscoveryHandler(Channel<DiscoveredTest> channel, ILogger log) :
    ITestDiscoveryEventsHandler, ITestDiscoveryEventsHandler2
{
  public void HandleDiscoveredTests(IEnumerable<TestCase>? discoveredTestCases)
  {
    if (discoveredTestCases == null) return;

    foreach (var tc in discoveredTestCases)
    {
      log.LogInformation("[+] Discovered {name}", tc.DisplayName);
      channel.Writer.TryWrite(tc.ToDiscoveredTest());
    }
  }

  public void HandleDiscoveryComplete(long totalTests, IEnumerable<TestCase>? lastChunk, bool aborted)
  {
    if (lastChunk != null)
    {
      foreach (var tc in lastChunk)
        channel.Writer.TryWrite(tc.ToDiscoveredTest());
    }

    channel.Writer.TryComplete();
  }

  public void HandleDiscoveryComplete(DiscoveryCompleteEventArgs args, IEnumerable<TestCase>? lastChunk)
      => HandleDiscoveryComplete(args.TotalCount, lastChunk, args.IsAborted);

  public void HandleLogMessage(TestMessageLevel level, string? message) { }
  public void HandleRawMessage(string rawMessage) { }
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