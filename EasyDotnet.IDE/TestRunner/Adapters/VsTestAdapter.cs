using System.IO.Abstractions;
using System.Threading.Channels;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Sdk;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Settings;
using EasyDotnet.IDE.TestRunner.Adapters.VSTest;
using EasyDotnet.IDE.TestRunner.Lock;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Store;
using Microsoft.Extensions.Logging;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;

namespace EasyDotnet.IDE.TestRunner.Adapters;

/// <summary>
/// Wraps VSTest console for a single project TFM.
/// The VsTestConsoleWrapper is kept warm between operations and only
/// recreated when the adapter is disposed (on invalidate).
/// The global operation lock makes the old VsTestService semaphore redundant.
/// </summary>
public sealed class VsTestAdapter(
    SdkService sdkService,
    IEditorService editorService,
    IDebugStrategyFactory debugStrategyFactory,
    IDebugOrchestrator debugOrchestrator,
    IFileSystem fileSystem,
    SettingsService settingsService,
    ILoggerFactory loggerFactory) : ITestAdapter, IAsyncDisposable
{
  private readonly ILogger _logger = loggerFactory.CreateLogger<VsTestAdapter>();
  private static readonly TestPlatformOptions DefaultOptions = new()
  {
    CollectMetrics = false,
    SkipDefaultAdapters = false
  };

  private readonly SemaphoreSlim _gate = new(1, 1);

  private VsTestConsoleWrapper? _wrapper;

  // TestCases from the last complete discovery, reused by RunCoreAsync so a run doesn't pay
  // for a second full VSTest discovery. Dropped when the assembly stamp changes, on dispose
  // and whenever the wrapper is killed or recreated.
  private DiscoveredTestCases? _testCases;

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

      await using var _ = ct.Register(() =>
      {
        try { wrapper.CancelDiscovery(); } catch { /* best effort */ }
      });

      _testCases = null;
      var stamp = AssemblyStamp.TryCapture(fileSystem, project.TargetPath);

      var handler = new StreamingDiscoveryHandler(loggerFactory);
      wrapper.DiscoverTests([project.TargetPath], null, DefaultOptions, null, handler);

      // Buffer all TestCases so we can detect parameterised rows by duplicate FQN —
      // this is the only reliable VSTest-layer signal, since frameworks like MSTest
      // can strip parens from DisplayName entirely via [DataRow(DisplayName = "...")].
      var testCases = new List<TestCase>();
      await foreach (var tc in handler.ReadAllAsync().WithCancellation(ct))
      {
        testCases.Add(tc);
      }

      if (!handler.IsAborted && stamp is { } s && ReferenceEquals(_wrapper, wrapper))
      {
        _testCases = new DiscoveredTestCases(
            project.TargetPath,
            project.TargetFramework,
            s,
            testCases.DistinctBy(tc => tc.Id).ToDictionary(tc => tc.Id));
      }

      foreach (var group in testCases.GroupBy(tc => tc.FullyQualifiedName))
      {
        var isParameterised = group.Count() > 1;
        foreach (var tc in group)
        {
          var discoveredTest = tc.ToDiscoveredTest(isParameterised);
          try { await onDiscovered(discoveredTest); }
          catch (Exception ex) { _logger.LogError(ex, "Error in onDiscovered callback for {TestCase}", discoveredTest.FullyQualifiedName); }
        }
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  public async Task RunAsync(
      ValidatedDotnetProject project,
      IReadOnlyList<string> nativeIds,
      Func<string, Task> onStarted,
      Func<TestRunResult, Task> onResult,
      OperationControl control,
      CancellationToken ct)
  {
    var nativeGuids = nativeIds.Select(Guid.Parse).ToHashSet();
    await RunCoreAsync(project, nativeGuids, onStarted, onResult, attachDebugger: false, control, ct);
  }

  public async Task DebugAsync(
      ValidatedDotnetProject project,
      IReadOnlyList<string> nativeIds,
      Func<string, Task> onStarted,
      Func<TestRunResult, Task> onResult,
      OperationControl control,
      CancellationToken ct)
  {
    var nativeGuids = nativeIds.Select(Guid.Parse).ToHashSet();
    await RunCoreAsync(project, nativeGuids, onStarted, onResult, attachDebugger: true, control, ct);
  }

  private async Task RunCoreAsync(
      ValidatedDotnetProject project,
      HashSet<Guid> nativeGuids,
      Func<string, Task> onStarted,
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

      var channel = Channel.CreateUnbounded<VsTestRunEvent>();

      var toRun = GetCachedTestCases(project, nativeGuids) ?? DiscoverTestCases(wrapper, project, nativeGuids);

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
        await foreach (var runEvent in channel.Reader.ReadAllAsync(runToken))
        {
          if (runEvent.StartedNativeId is { } startedNativeId)
          {
            await onStarted(startedNativeId);
            continue;
          }

          if (runEvent.TestResult is { } result)
            await onResult(result);
        }

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
    var vsTestPath = sdkService.GetVsTestPath();
    _testCases = null;
    _wrapper = new VsTestConsoleWrapper(vsTestPath);
    return _wrapper;
  }

  /// <summary>
  /// Returns the requested TestCases from the last discovery, or null on a cache miss
  /// (different assembly, assembly rebuilt since, or any requested id unknown).
  /// </summary>
  private List<TestCase>? GetCachedTestCases(ValidatedDotnetProject project, HashSet<Guid> nativeGuids)
  {
    var cached = _testCases;
    if (cached is null
        || !string.Equals(cached.TargetPath, project.TargetPath, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(cached.TargetFramework, project.TargetFramework, StringComparison.OrdinalIgnoreCase)
        || AssemblyStamp.TryCapture(fileSystem, project.TargetPath) != cached.Stamp)
    {
      return null;
    }

    var toRun = nativeGuids
        .Select(id => cached.ById.GetValueOrDefault(id))
        .OfType<TestCase>()
        .ToList();

    if (toRun.Count == nativeGuids.Count) return toRun;

    _logger.LogDebug("TestCase cache missing {Missing} of {Requested} requested tests — re-discovering",
        nativeGuids.Count - toRun.Count, nativeGuids.Count);
    return null;
  }

  private List<TestCase> DiscoverTestCases(VsTestConsoleWrapper wrapper, ValidatedDotnetProject project, HashSet<Guid> nativeGuids)
  {
    var discoveryHandler = new TestDiscoveryHandler(loggerFactory.CreateLogger<TestDiscoveryHandler>());
    wrapper.DiscoverTests([project.TargetPath], null, DefaultOptions, null, discoveryHandler);

    return discoveryHandler.TestCases
        .Where(x => nativeGuids.Contains(x.Id))
        .ToList();
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
      await session.WaitForDebugSessionStartedAsync().WaitAsync(ct);
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
    _testCases = null;
    return ValueTask.CompletedTask;
  }

  private sealed record DiscoveredTestCases(
      string TargetPath,
      string? TargetFramework,
      AssemblyStamp Stamp,
      Dictionary<Guid, TestCase> ById);

  private Task KillWrapperAsync(VsTestConsoleWrapper wrapper)
  {
    return Task.Run(() =>
    {
      try { wrapper.AbortTestRun(); } catch { /* best effort */ }

      try { wrapper.EndSession(); } catch { /* best effort */ }

      if (ReferenceEquals(_wrapper, wrapper))
      {
        _wrapper = null;
        _testCases = null;
      }
    });
  }
}