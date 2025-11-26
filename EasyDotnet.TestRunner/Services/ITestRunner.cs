using EasyDotnet.TestRunner.Models;
using EasyDotnet.TestRunner.Notifications;
using EasyDotnet.TestRunner.Requests;

namespace EasyDotnet.TestRunner.Services;

public interface ITestRunner
{
  Task DebugTestsAsync(DebugRequest request, CancellationToken cancellationToken);
  Task InitializeAsync(string solutionFilePath, CancellationToken cancellationToken);
  Task RunTestsAsync(RunRequest request, CancellationToken cancellationToken);
  Task StartDiscoveryAsync(CancellationToken cancellationToken);
}