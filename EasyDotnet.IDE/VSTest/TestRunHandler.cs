using System.Threading.Channels;
using EasyDotnet.IDE.TestRunner.Adapters;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.IDE.VSTest;

internal sealed class TestRunHandler(
    ChannelWriter<TestRunResult> writer,
    ILogger<TestRunHandler> logger) : ITestRunEventsHandler
{
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
      writer.TryWrite(mapped);
    }
  }
}