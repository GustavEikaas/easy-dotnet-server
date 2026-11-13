namespace EasyDotnet.IDE.TestRunner.Adapters;

/// <summary>
/// Serializes access to vstest.console across all <see cref="VsTestAdapter"/> instances.
///
/// VsTestAdapter is registered transient (one instance per project), each with its own
/// VsTestConsoleWrapper, so the adapter's own per-instance gate only prevents concurrent
/// calls against the *same* project. When a solution has multiple test projects,
/// TestRunnerService.InitializeAsync discovers them all concurrently (Task.WhenAll), which
/// previously meant that many concurrent VsTestConsoleWrapper sessions were started in-process
/// via Microsoft.TestPlatform.VsTestConsole.TranslationLayer at once — observed to hang/never
/// return once three or more run simultaneously.
///
/// This mirrors the global semaphore the old singleton VsTestService used to carry (removed
/// when that service was refactored into the current per-project ITestAdapter/AdapterResolver
/// pattern), restoring it as a dedicated singleton gate instead.
/// </summary>
public sealed class VsTestConcurrencyGate
{
  private readonly SemaphoreSlim _semaphore = new(1, 1);

  public async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct)
  {
    await _semaphore.WaitAsync(ct);
    return new Releaser(_semaphore);
  }

  private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
  {
    public ValueTask DisposeAsync()
    {
      semaphore.Release();
      return ValueTask.CompletedTask;
    }
  }
}
