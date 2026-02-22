using System.Threading.Channels;
using EasyDotnet.Types;
using EasyDotnet.VSTest;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.IDE.VSTest;

internal sealed class TestRunHandler(ChannelWriter<TestRunResult> writer) : ITestRunEventsHandler
{
  public void HandleLogMessage(TestMessageLevel level, string? message) { }

  public void HandleRawMessage(string rawMessage) { }

  public void HandleTestRunComplete(TestRunCompleteEventArgs testRunCompleteArgs, TestRunChangedEventArgs? lastChunkArgs, ICollection<AttachmentSet>? runContextAttachments, ICollection<string>? executorUris)
  {
    if (lastChunkArgs?.NewTestResults is not null)
    {
      foreach (var result in lastChunkArgs.NewTestResults)
      {
        writer.TryWrite(result.ToTestRunResult());
      }
    }
  }

  public void HandleTestRunStatsChange(TestRunChangedEventArgs? testRunChangedArgs)
  {
    if (testRunChangedArgs?.NewTestResults is not null)
    {
      foreach (var result in testRunChangedArgs.NewTestResults)
      {
        // Push to the channel immediately
        writer.TryWrite(result.ToTestRunResult());
      }
    }
  }

  public int LaunchProcessWithDebuggerAttached(TestProcessStartInfo testProcessStartInfo) => throw new NotImplementedException();
}