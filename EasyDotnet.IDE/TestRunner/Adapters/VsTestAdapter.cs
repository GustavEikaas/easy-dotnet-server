using System.Threading.Channels;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.VSTest;
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
    ILoggerFactory loggerFactory) : ITestAdapter, IAsyncDisposable
{
  private static readonly TestPlatformOptions DefaultOptions = new()
  {
    CollectMetrics = false,
    SkipDefaultAdapters = false
  };

  private VsTestConsoleWrapper? _wrapper;

  public Task DiscoverAsync(string dllPath, Func<DiscoveredTest, Task> onDiscovered, CancellationToken ct)
  {
    var wrapper = EnsureWrapper();
    var handler = new StreamingDiscoveryHandler(onDiscovered, loggerFactory);
    wrapper.DiscoverTests([dllPath], null, DefaultOptions, null, handler);
    return handler.Completion;
  }

  public async Task RunAsync(
      string dllPath,
      IReadOnlyList<string> nativeIds,
      Func<TestRunResult, Task> onResult,
      CancellationToken ct)
  {
    var nativeGuids = nativeIds.Select(Guid.Parse).ToHashSet();
    await RunCoreAsync(dllPath, nativeGuids, onResult, attachDebugger: false, ct);
  }

  public async Task DebugAsync(
      string dllPath,
      IReadOnlyList<string> nativeIds,
      Func<TestRunResult, Task> onResult,
      CancellationToken ct)
  {
    var nativeGuids = nativeIds.Select(Guid.Parse).ToHashSet();
    await RunCoreAsync(dllPath, nativeGuids, onResult, attachDebugger: true, ct);
  }

  private async Task RunCoreAsync(
      string dllPath,
      HashSet<Guid> nativeGuids,
      Func<TestRunResult, Task> onResult,
      bool attachDebugger,
      CancellationToken ct)
  {
    var wrapper = EnsureWrapper();
    var channel = Channel.CreateUnbounded<TestRunResult>();

    // Re-discover to get TestCase objects needed by VSTest run API
    var discoveryHandler = new TestDiscoveryHandler(loggerFactory.CreateLogger<TestDiscoveryHandler>());
    wrapper.DiscoverTests([dllPath], null, DefaultOptions, null, discoveryHandler);
    var toRun = discoveryHandler.TestCases
        .Where(x => nativeGuids.Contains(x.Id))
        .ToList();

    if (toRun.Count == 0) return;

    var runHandler = new TestRunHandler(channel.Writer);

    var runTask = Task.Run(() =>
    {
      try
      {
        if (attachDebugger)
          wrapper.RunTestsWithCustomTestHost(toRun, null, DefaultOptions, null, runHandler, CreateDebuggerLauncher());
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

  private DebuggerTestHostLauncher CreateDebuggerLauncher()
  {
    // TODO: wire debug orchestrator — injected in a follow-up once
    // the debugger strategy is integrated with the new TestRunnerService
    throw new NotImplementedException("Debug launcher not yet wired in VsTestAdapter");
  }

  public ValueTask DisposeAsync()
  {
    _wrapper = null;
    return ValueTask.CompletedTask;
  }
}