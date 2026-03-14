using System.Threading.Channels;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.TestRunner.Adapters.VSTest;
using EasyDotnet.IDE.TestRunner.Lock;
using EasyDotnet.IDE.TestRunner.Models;
using Microsoft.Extensions.Logging;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;

namespace EasyDotnet.IDE.TestRunner.Adapters;

/// <summary>
/// Wraps VSTest console for a single project TFM.
/// The VsTestConsoleWrapper is kept warm between operations and only
/// recreated when the adapter is disposed (on invalidate).
/// The global operation lock makes the old VsTestService semaphore redundant.
/// </summary>
public sealed class VsTestAdapter(
    IMsBuildService msBuildService,
    IEditorService editorService,
    IDebugStrategyFactory debugStrategyFactory,
    IDebugOrchestrator debugOrchestrator,
    ILoggerFactory loggerFactory) : ITestAdapter, IAsyncDisposable
{
  private static readonly TestPlatformOptions DefaultOptions = new()
  {
    CollectMetrics = false,
    SkipDefaultAdapters = false
  };

  private VsTestConsoleWrapper? _wrapper;

  public async Task DiscoverAsync(
      ValidatedDotnetProject project,
      Func<DiscoveredTest, Task> onDiscovered,
      OperationControl control,
      CancellationToken ct)
  {
    var wrapper = EnsureWrapper();
    control.RegisterKill(() => KillWrapperAsync(wrapper));

    using var _ = ct.Register(() =>
    {
      try { wrapper.CancelDiscovery(); } catch { /* best effort */ }
    });

    var handler = new StreamingDiscoveryHandler(onDiscovered, loggerFactory);
    wrapper.DiscoverTests([project.TargetPath], null, DefaultOptions, null, handler);
    await handler.Completion;
  }

  public async Task RunAsync(
      ValidatedDotnetProject project,
      IReadOnlyList<string> nativeIds,
      Func<TestRunResult, Task> onResult,
      OperationControl control,
      CancellationToken ct)
  {
    var nativeGuids = nativeIds.Select(Guid.Parse).ToHashSet();
    await RunCoreAsync(project, nativeGuids, onResult, attachDebugger: false, control, ct);
  }

  public async Task DebugAsync(
      ValidatedDotnetProject project,
      IReadOnlyList<string> nativeIds,
      Func<TestRunResult, Task> onResult,
      OperationControl control,
      CancellationToken ct)
  {
    var nativeGuids = nativeIds.Select(Guid.Parse).ToHashSet();
    await RunCoreAsync(project, nativeGuids, onResult, attachDebugger: true, control, ct);
  }

  private async Task RunCoreAsync(
      ValidatedDotnetProject project,
      HashSet<Guid> nativeGuids,
      Func<TestRunResult, Task> onResult,
      bool attachDebugger,
      OperationControl control,
      CancellationToken ct)
  {
    var wrapper = EnsureWrapper();
    control.RegisterKill(() => KillWrapperAsync(wrapper));

    var channel = Channel.CreateUnbounded<TestRunResult>();

    // Re-discover to get TestCase objects needed by VSTest run API
    var discoveryHandler = new TestDiscoveryHandler(loggerFactory.CreateLogger<TestDiscoveryHandler>());
    wrapper.DiscoverTests([project.TargetPath], null, DefaultOptions, null, discoveryHandler);
    var toRun = discoveryHandler.TestCases
        .Where(x => nativeGuids.Contains(x.Id))
        .ToList();

    if (toRun.Count == 0) return;

    var runHandler = new TestRunHandler(channel.Writer, loggerFactory.CreateLogger<TestRunHandler>());

    //TODO: add runsettings
    // var project = await GetProject(projectPath, targetFrameworkMoniker, configuration, token);
    //
    // var runSettingsFile = settingsService.GetProjectRunSettings(projectPath!);
    // var runSettings = runSettingsFile is not null ? fileSystem.File.ReadAllText(runSettingsFile) : null;

    var runTask = Task.Run(() =>
    {
      try
      {
        if (attachDebugger)
          wrapper.RunTestsWithCustomTestHost(toRun, null, DefaultOptions, null, runHandler, CreateDebuggerLauncher(project));
        else
          wrapper.RunTests(toRun, null, DefaultOptions, null, runHandler);
      }
      catch (Exception ex)
      {
        channel.Writer.TryComplete(ex);
      }
      finally
      {
        channel.Writer.TryComplete();
      }
    }, ct);

    await using var _ = ct.Register(() =>
    {
      try { wrapper.CancelTestRun(); } catch { /* best effort */ }
    });

    await foreach (var result in channel.Reader.ReadAllAsync(ct))
      await onResult(result);

    await runTask;
  }

  private VsTestConsoleWrapper EnsureWrapper()
  {
    if (_wrapper is not null) return _wrapper;
    var vsTestPath = GetVsTestPath();
    _wrapper = new VsTestConsoleWrapper(vsTestPath);
    return _wrapper;
  }

  private string GetVsTestPath()
  {
    var sdk = msBuildService.QuerySdkInstallations();
    return Path.Join(sdk.ToList()[0].MSBuildPath, "vstest.console.dll");
  }

  private DebuggerTestHostLauncher CreateDebuggerLauncher(ValidatedDotnetProject project) =>
    new(async (pid, ct) =>
    {
      var session = await debugOrchestrator.StartClientDebugSessionAsync(project.ProjectFullPath, new(project.ProjectFullPath, project.TargetFramework, null, null), debugStrategyFactory.CreateStandardAttachStrategy(pid), ct);
      await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
      await session.ProcessStarted;
      //We add a delay to ensure the client is ready #gh785
      await Task.Delay(1000, ct);
      //This would replace the ProcessStarted and delay but we need help to regression test it
      // await session.WaitForConfigurationDoneAsync();
      return true;
    });

  public ValueTask DisposeAsync()
  {
    _wrapper = null;
    return ValueTask.CompletedTask;
  }

  private Task KillWrapperAsync(VsTestConsoleWrapper wrapper)
  {
    return Task.Run(() =>
    {
      try { wrapper.AbortTestRun(); } catch { /* best effort */ }

      try { wrapper.EndSession(); } catch { /* best effort */ }

      if (ReferenceEquals(_wrapper, wrapper))
        _wrapper = null;
    });
  }
}