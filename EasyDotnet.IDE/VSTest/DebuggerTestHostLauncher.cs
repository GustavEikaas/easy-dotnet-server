using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client.Interfaces;

namespace EasyDotnet.IDE.VSTest;

internal class DebuggerTestHostLauncher(Func<int, CancellationToken, Task<bool>> attach) : ITestHostLauncher2
{
  public bool IsDebug => true;

  public bool AttachDebuggerToProcess(int pid) => attach(pid, CancellationToken.None).GetAwaiter().GetResult();

  public bool AttachDebuggerToProcess(int pid, CancellationToken cancellationToken) => attach(pid, cancellationToken).GetAwaiter().GetResult();

  public int LaunchTestHost(TestProcessStartInfo defaultTestHostStartInfo) => throw new NotImplementedException("LaunchTestHost not implemented yet");

  public int LaunchTestHost(TestProcessStartInfo defaultTestHostStartInfo, CancellationToken cancellationToken) => throw new NotImplementedException("LaunchTestHost not implemented yet");
}