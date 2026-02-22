using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.VSTest;
using EasyDotnet.MsBuild;
using EasyDotnet.Types;
using EasyDotnet.VSTest;
using Microsoft.Extensions.Logging;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;

namespace EasyDotnet.IDE.Services;

public class VsTestService(
  IMsBuildService msBuildService,
  ILogger<VsTestService> logService,
  IEditorService editorService,
  ILoggerFactory loggerFactory,
  IDebugStrategyFactory debugStrategyFactory,
  IDebugOrchestrator debugOrchestrator)
{
  private readonly TimeSpan _queueTimeout = TimeSpan.FromMinutes(5);
  private readonly SemaphoreSlim _vstestLock = new(1, 1);
  private static readonly TestPlatformOptions DefaultOptions = new() { CollectMetrics = false, SkipDefaultAdapters = false };

  public async Task<List<DiscoveredTest>> RunDiscover(string dllPath, CancellationToken cancellationToken)
  {
    await _vstestLock.WaitAsync(_queueTimeout, cancellationToken);
    try
    {
      var vsTestPath = GetVsTestPath();
      logService.LogInformation("Using VSTest path: {vsTestPath}", vsTestPath);
      var discoveredTests = Discover(vsTestPath, [dllPath]);
      discoveredTests.TryGetValue(Path.GetFileName(dllPath), out var tests);
      return tests ?? [];
    }
    finally
    {
      _vstestLock.Release();
    }
  }

  public async IAsyncEnumerable<TestRunResult> RunTests(
    DotnetProject project,
    Guid[] testIds,
    string? runSettings,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    await _vstestLock.WaitAsync(_queueTimeout, cancellationToken);
    try
    {
      var vsTestPath = GetVsTestPath();
      logService.LogInformation("Using VSTest path: {vsTestPath}", vsTestPath);

      await foreach (var result in RunTestsAsync(vsTestPath, project, testIds, runSettings, attachDebugger: false, cancellationToken))
      {
        yield return result;
      }
    }
    finally
    {
      _vstestLock.Release();
    }
  }

  public async IAsyncEnumerable<TestRunResult> DebugTests(
    DotnetProject project,
    Guid[] testIds,
    string? runSettings,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    await _vstestLock.WaitAsync(_queueTimeout, cancellationToken);
    try
    {
      var vsTestPath = GetVsTestPath();
      logService.LogInformation("Using VSTest path: {vsTestPath}", vsTestPath);

      await foreach (var result in RunTestsAsync(vsTestPath, project, testIds, runSettings, attachDebugger: true, cancellationToken))
      {
        yield return result;
      }
    }
    finally
    {
      _vstestLock.Release();
    }
  }

  private Dictionary<string, List<DiscoveredTest>> Discover(string vsTestConsolePath, string[] testDllPath)
  {
    var runner = new VsTestConsoleWrapper(vsTestConsolePath);
    var sessionHandler = new TestSessionHandler();
    var discoveryHandler = new TestDiscoveryHandler(loggerFactory.CreateLogger<TestDiscoveryHandler>());
    runner.DiscoverTests(testDllPath, null, DefaultOptions, sessionHandler.TestSessionInfo, discoveryHandler);

    return discoveryHandler.TestCases.GroupBy(x => Path.GetFileName(x.Source)).ToDictionary(x => x.Key, y => y.Select(x => x.ToDiscoveredTest()).ToList());
  }

  private async IAsyncEnumerable<TestRunResult> RunTestsAsync(
      string vsTestPath,
      DotnetProject project,
      Guid[] testIds,
      string? runSettings,
      bool attachDebugger,
      [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    var channel = Channel.CreateUnbounded<TestRunResult>();
    var testHost = new VsTestConsoleWrapper(vsTestPath);

    var testExecutionTask = Task.Run(() =>
    {
      try
      {
        var discoveryHandler = new TestDiscoveryHandler(loggerFactory.CreateLogger<TestDiscoveryHandler>());
        var sessionHandler = new TestSessionHandler();
        var runHandler = new TestRunHandler(channel.Writer);

        testHost.DiscoverTests([project.TargetPath!], null, DefaultOptions, sessionHandler.TestSessionInfo, discoveryHandler);
        var runTests = discoveryHandler.TestCases.Where(x => testIds.Contains(x.Id)).ToList();

        if (runTests.Count == 0) return;

        if (attachDebugger)
        {
          testHost.RunTestsWithCustomTestHost(runTests, runSettings, DefaultOptions, sessionHandler.TestSessionInfo, runHandler, CreateDebuggerLauncher(project));
        }
        else
        {
          testHost.RunTests(runTests, runSettings, DefaultOptions, sessionHandler.TestSessionInfo, runHandler);
        }
      }
      catch (Exception ex)
      {
        channel.Writer.TryComplete(ex);
      }
      finally
      {
        channel.Writer.TryComplete();
      }
    }, cancellationToken);

    await using var _ = cancellationToken.Register(() =>
    {
      try { testHost.CancelTestRun(); } catch { }
    });

    await foreach (var result in channel.Reader.ReadAllAsync(cancellationToken))
    {
      yield return result;
    }

    await testExecutionTask;
  }

  private DebuggerTestHostLauncher CreateDebuggerLauncher(DotnetProject project) =>
    new(async (pid, ct) =>
    {
      var session = await debugOrchestrator.StartClientDebugSessionAsync(project.MSBuildProjectFullPath!, new(project.MSBuildProjectFullPath!, null, null, null), debugStrategyFactory.CreateStandardAttachStrategy(pid), ct);
      await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
      await session.ProcessStarted;
      //We add a delay to ensure the client is ready #gh785
      await Task.Delay(1000, ct);
      //This would replace the ProcessStarted and delay but we need help to regression test it
      // await session.WaitForConfigurationDoneAsync();
      return true;
    });

  private string GetVsTestPath()
  {
    var sdk = msBuildService.QuerySdkInstallations();
    return Path.Join(sdk.ToList()[0].MSBuildPath, "vstest.console.dll");
  }
}