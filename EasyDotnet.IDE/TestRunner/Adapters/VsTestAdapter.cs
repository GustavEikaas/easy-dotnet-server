using System.IO.Abstractions;
using System.Threading;
using System.Threading.Channels;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Settings;
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
    IFileSystem fileSystem,
    SettingsService settingsService,
    ILoggerFactory loggerFactory) : ITestAdapter, IAsyncDisposable
{
  private static readonly TestPlatformOptions DefaultOptions = new()
  {
    CollectMetrics = false,
    SkipDefaultAdapters = false
  };

  private readonly SemaphoreSlim _gate = new(1, 1);

  private VsTestConsoleWrapper? _wrapper;

  public async Task DiscoverAsync(
      ValidatedDotnetProject project,
      Func<DiscoveredTest, Task> onDiscovered,
      OperationControl control,
      CancellationToken ct)
  {
    await _gate.WaitAsync(ct);
    try
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
    finally
    {
      _gate.Release();
    }
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
    await _gate.WaitAsync(ct);
    try
    {
      var wrapper = EnsureWrapper();
      control.RegisterKill(() => KillWrapperAsync(wrapper));

      using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      var runToken = runCts.Token;

      var debugStopped = 0;
      void OnDebugStopped()
      {
        if (Interlocked.Exchange(ref debugStopped, 1) == 1) return;
        try { runCts.Cancel(); } catch { }
        try { wrapper.CancelTestRun(); } catch { /* best effort */ }
      }

      var channel = Channel.CreateUnbounded<TestRunResult>();

      // Re-discover to get TestCase objects needed by VSTest run API.
      var discoveryHandler = new TestDiscoveryHandler(loggerFactory.CreateLogger<TestDiscoveryHandler>());
      wrapper.DiscoverTests([project.TargetPath], null, DefaultOptions, null, discoveryHandler);

      var toRun = discoveryHandler.TestCases
          .Where(x => nativeGuids.Contains(x.Id))
          .ToList();

      if (toRun.Count == 0) return;

      var runHandler = new TestRunHandler(channel.Writer, loggerFactory.CreateLogger<TestRunHandler>());

      string? runSettings = null;
      try
      {
        var runSettingsFile = settingsService.GetProjectRunSettings(project.ProjectFullPath);
        if (!string.IsNullOrWhiteSpace(runSettingsFile) && fileSystem.File.Exists(runSettingsFile))
        {
          runSettings = fileSystem.File.ReadAllText(runSettingsFile);
        }
      }
      catch (Exception ex)
      {
        loggerFactory.CreateLogger<VsTestAdapter>().LogDebug(ex, "Failed to read runsettings file (ignored)");
      }

      var runTask = Task.Run(() =>
      {
        try
        {
          if (attachDebugger)
          {
            var launcher = CreateDebuggerLauncher(project, OnDebugStopped);
            wrapper.RunTestsWithCustomTestHost(toRun, runSettings, DefaultOptions, null, runHandler, launcher);
          }
          else
          {
            wrapper.RunTests(toRun, runSettings, DefaultOptions, null, runHandler);
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
      }, runToken);

      await using var registration = runToken.Register(() =>
      {
        try { wrapper.CancelTestRun(); } catch { /* best effort */ }
      });

      try
      {
        await foreach (var result in channel.Reader.ReadAllAsync(runToken))
          await onResult(result);

        await runTask;
      }
      catch (Exception ex) when (Volatile.Read(ref debugStopped) == 1 && IsVsTestDisconnectOnAbort(ex))
      {
        throw new OperationCanceledException("Debug session ended", ex, runToken);
      }
      finally
      {
        if (runToken.IsCancellationRequested && !runTask.IsCompleted)
        {
          try { wrapper.CancelTestRun(); } catch { }

          var finished = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(2)));
          if (!ReferenceEquals(finished, runTask))
          {
            await KillWrapperAsync(wrapper);
            _ = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(2)));
          }
        }
      }
    }
    finally
    {
      _gate.Release();
    }
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

  private DebuggerTestHostLauncher CreateDebuggerLauncher(
      ValidatedDotnetProject project,
      Action onStopped) =>
    new(async (pid, ct) =>
    {
      var session = await debugOrchestrator.StartClientDebugSessionAsync(
          project.ProjectFullPath,
          debugStrategyFactory.CreateStandardAttachStrategy(pid, project.Raw.ProjectDir),
          ct);

      session.Stopped += onStopped;
      if (session.StoppedTask.IsCompleted) onStopped();

      await editorService.RequestStartDebugSession("127.0.0.1", session.Port);
      await session.ProcessStarted;
      //We add a delay to ensure the client is ready #gh785
      await Task.Delay(1000, ct);
      //This would replace the ProcessStarted and delay but we need help to regression test it
      // await session.WaitForConfigurationDoneAsync();
      return true;
    });

  private static bool IsVsTestDisconnectOnAbort(Exception ex)
  {
    while (true)
    {
      if (ex is AggregateException { InnerExceptions.Count: 1 } agg)
      {
        ex = agg.InnerExceptions[0];
        continue;
      }

      if (ex is ChannelClosedException { InnerException: not null } cc)
      {
        ex = cc.InnerException!;
        continue;
      }

      break;
    }

    return ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException;
  }

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