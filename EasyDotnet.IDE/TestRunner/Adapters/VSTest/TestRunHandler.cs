using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.IDE.TestRunner.Adapters.VSTest;

internal sealed class TestRunHandler(
    ChannelWriter<VsTestRunEvent> writer,
    ILogger<TestRunHandler> logger) : ITestRunEventsHandler
{
  private readonly HashSet<Guid> _started = [];
  private readonly object _startedLock = new();

  public void HandleLogMessage(TestMessageLevel level, string? message) { }
  public void HandleRawMessage(string rawMessage) { }

  public void HandleTestRunComplete(
      TestRunCompleteEventArgs testRunCompleteArgs,
      TestRunChangedEventArgs? lastChunkArgs,
      ICollection<AttachmentSet>? runContextAttachments,
      ICollection<string>? executorUris)
  {
    if (lastChunkArgs?.NewTestResults is not null)
      WriteResults(lastChunkArgs.NewTestResults);
  }

  public void HandleTestRunStatsChange(TestRunChangedEventArgs? testRunChangedArgs)
  {
    if (testRunChangedArgs?.ActiveTests is not null)
      WriteStarted(testRunChangedArgs.ActiveTests);

    if (testRunChangedArgs?.NewTestResults is not null)
      WriteResults(testRunChangedArgs.NewTestResults);
  }

  public int LaunchProcessWithDebuggerAttached(TestProcessStartInfo testProcessStartInfo) =>
      throw new NotImplementedException();

  private void WriteResults(IEnumerable<TestResult> results)
  {
    foreach (var result in results)
    {
      var mapped = result.ToTestRunResult();
      if (mapped is null)
      {
        logger.LogError("Skipping result for {TestId} — unmapped VSTest outcome: {Outcome}", result.TestCase.Id, result.Outcome);
        continue;
      }
      writer.TryWrite(VsTestRunEvent.Result(mapped));
    }
  }

  private void WriteStarted(IEnumerable<TestCase> tests)
  {
    foreach (var test in tests)
    {
      lock (_startedLock)
      {
        if (!_started.Add(test.Id)) continue;
      }

      writer.TryWrite(VsTestRunEvent.Started(test.Id.ToString()));
    }
  }
}

internal sealed record VsTestRunEvent(string? StartedNativeId, TestRunResult? TestResult)
{
  public static VsTestRunEvent Started(string nativeId) => new(nativeId, null);
  public static VsTestRunEvent Result(TestRunResult result) => new(null, result);
}