using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace EasyDotnet.VSTest;
internal class TestSessionHandler : ITestSessionEventsHandler
{
  public TestSessionHandler() { }
  public TestSessionInfo TestSessionInfo { get; private set; }

  public void HandleLogMessage(TestMessageLevel level, string message) { }
  public void HandleRawMessage(string rawMessage) { }
  public void HandleStartTestSessionComplete(StartTestSessionCompleteEventArgs eventArgs) => TestSessionInfo = eventArgs?.TestSessionInfo;
  public void HandleStopTestSessionComplete(StopTestSessionCompleteEventArgs eventArgs) { }
}