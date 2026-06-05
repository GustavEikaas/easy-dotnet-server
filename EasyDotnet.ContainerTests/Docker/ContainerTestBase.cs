using System.Threading.Channels;
using EasyDotnet.ContainerTests.Scaffold;
using EasyDotnet.ContainerTests.TestRunner;
using EasyDotnet.IDE.Models.Client;
using StreamJsonRpc;
using Xunit.Sdk;

namespace EasyDotnet.ContainerTests.Docker;

/// <summary>
/// Common lifecycle and helpers shared by all container integration tests.
/// Sets <see cref="ServerContainer.RpcConfigurator"/> via <see cref="ConfigureRpc"/> before
/// starting the container so reverse-request handlers are registered before the first message.
/// </summary>
public abstract class ContainerTestBase<TContainer> : IAsyncLifetime, ISharedContainerRpcTarget
  where TContainer : ServerContainer, new()
{
  private static readonly TestClientInfo DefaultClientInfo = new("test", "3.0.0");
  private static readonly TimeSpan DefaultRpcTimeout = TimeSpan.FromMinutes(3);
  private static readonly TimeSpan DefaultAfterScopeGrace = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(250);

  private SharedContainerLease<TContainer>? _lease;

  protected TContainer Container => _lease?.Container
    ?? throw new InvalidOperationException("The shared container has not been leased for this test yet.");

  public async Task InitializeAsync()
  {
    _lease = await SharedContainerFixtureRegistry.Get<TContainer>().LeaseAsync(this);
  }

  public async Task DisposeAsync()
  {
    if (_lease is not null)
      await _lease.DisposeAsync();
  }

  /// <summary>Override to register reverse-request handlers on the RPC connection.</summary>
  protected virtual void ConfigureRpc(JsonRpc rpc) { }

  public virtual Task<string?> PromptSelectionAsync(TestPromptSelectionRequest request) =>
    throw new NotSupportedException("This test did not expect promptSelection.");

  public virtual Task<string[]?> PromptMultiSelectionAsync(TestPromptSelectionRequest request) =>
    throw new NotSupportedException("This test did not expect promptMultiSelection.");

  public virtual Task<string?> PromptStringAsync(TestPromptStringRequest request) =>
    throw new NotSupportedException("This test did not expect promptString.");

  public virtual Task<TestPickerResult?> PickerPickAsync(TestPickerRequest request) =>
    throw new NotSupportedException("This test did not expect picker/pick.");

  public virtual Task<bool> OpenBufferAsync(TestOpenBufferRequest request) =>
    throw new NotSupportedException("This test did not expect openBuffer.");

  public virtual Task<RunCommandResponse> RunCommandManagedAsync(TestTrackedJob job) =>
    throw new NotSupportedException("This test did not expect runCommandManaged.");

  public virtual void DisplayMessage(TestDisplayMessage message) { }

  public virtual void DisplayError(TestDisplayMessage message) { }

  public virtual void SetQuickFix(TestQuickFixItem[] quickFixItems) { }

  public virtual void SetQuickFixSilent(TestQuickFixItem[] quickFixItems) { }

  public virtual void SolutionProjectsLoaded(TestSolutionProjectsLoadedNotification notification) { }

  public virtual void RegisterTest(TestRegisterTestPayload payload) { }

  public virtual void RemoveTest(TestRemoveTestPayload payload) { }

  public virtual void UpdateStatus(TestUpdateStatusPayload payload) { }

  public virtual void UpdateStatusBatch(TestUpdateStatusBatchPayload payload) { }

  public virtual void TestrunnerStatusUpdate(RunnerStatusDto status) { }

  public virtual bool IsVisible() => true;

  /// <summary>
  /// Tracks the active RPC call so that <c>ReceiveXxx</c> helpers in derived classes can race
  /// against it. If the scope task completes before an expected reverse request arrives, those
  /// helpers throw immediately instead of waiting for their full timeout.
  /// Set by calling <see cref="BeginCall"/>.
  /// </summary>
  protected Task? _rpcScope;

  /// <summary>
  /// Starts an RPC call, wraps it with a hard timeout, stores it as the active scope, and
  /// returns the wrapped task. If the server does not respond within <paramref name="timeout"/>
  /// the task faults with <see cref="TimeoutException"/> and the test fails immediately rather
  /// than hanging indefinitely in CI.
  /// Derived classes expose typed wrappers (e.g. <c>BeginRun</c>) that call this.
  /// </summary>
  protected Task BeginCall(Task task, TimeSpan? timeout = null)
  {
    var timedTask = task.WaitAsync(timeout ?? DefaultRpcTimeout);
    _rpcScope = timedTask;
    return timedTask;
  }

  /// <summary>
  /// Waits for a reverse request or notification and treats RPC-scope completion as a
  /// signal to perform a short final drain before failing. This avoids races where the
  /// scope and the channel delivery complete at nearly the same time in CI.
  /// </summary>
  protected async Task<T> ReceiveAsync<T>(
    ChannelReader<T> reader,
    string name,
    TimeSpan timeout,
    Func<string> collectDiagnostics,
    TimeSpan? afterScopeGrace = null)
  {
    if (reader.TryRead(out var buffered))
      return buffered;

    var scope = _rpcScope;
    var grace = afterScopeGrace ?? DefaultAfterScopeGrace;

    if (scope is not null)
    {
      if (scope.IsCompleted)
      {
        if (reader.TryRead(out var immediate))
          return immediate;

        try
        {
          return await reader.ReadAsync().AsTask().WaitAsync(grace);
        }
        catch (TimeoutException)
        {
          await scope;
          throw new XunitException(
            $"RPC scope completed without sending {name}.{collectDiagnostics()}");
        }
      }

      var readTask = reader.ReadAsync().AsTask();
      var timeoutTask = Task.Delay(timeout);
      var winner = await Task.WhenAny(readTask, scope, timeoutTask);

      if (winner == readTask)
        return await readTask;

      if (winner == timeoutTask)
        throw new XunitException(
          $"Timed out after {FormatTimeout(timeout)} waiting for {name}.{collectDiagnostics()}");

      if (readTask.IsCompletedSuccessfully)
        return readTask.Result;

      try
      {
        return await readTask.WaitAsync(grace);
      }
      catch (TimeoutException)
      {
        await scope;
        throw new XunitException(
          $"RPC scope completed without sending {name}.{collectDiagnostics()}");
      }
    }

    try
    {
      return await reader.ReadAsync().AsTask().WaitAsync(timeout);
    }
    catch (TimeoutException)
    {
      throw new XunitException(
        $"{name} not received within {FormatTimeout(timeout)}.{collectDiagnostics()}");
    }
  }

  /// <summary>
  /// Checks that no item is already queued and that none arrives during a short quiet period.
  /// Use only after the RPC scope for the operation has completed.
  /// </summary>
  protected bool IsNotReceived<T>(ChannelReader<T> reader, TimeSpan? quietPeriod = null)
  {
    if (reader.TryRead(out _))
      return false;

    Thread.Sleep(quietPeriod ?? DefaultQuietPeriod);
    return !reader.TryRead(out _);
  }

  private static string FormatTimeout(TimeSpan timeout) =>
    timeout >= TimeSpan.FromMinutes(1)
      ? $"{timeout.TotalMinutes:0} minutes"
      : $"{timeout.TotalSeconds:0} seconds";

  /// <summary>
  /// Initializes the server with the given workspace.
  /// When <see cref="TempWorkspace.SolutionPath"/> is non-null the server is pointed at that solution;
  /// otherwise heuristic project discovery is used (no solution file).
  /// </summary>
  protected Task<TestInitializeResponse> InitializeWorkspaceAsync(TempWorkspace ws) =>
    Container.Rpc.InvokeWithParameterObjectAsync<TestInitializeResponse>(
      "initialize",
      new List<TestInitializeRequest>
      {
        new(DefaultClientInfo, ws.SolutionPath is { } solutionPath
          ? new TestProjectInfo(Path.GetDirectoryName(solutionPath)!, solutionPath)
          : new TestProjectInfo(ws.RootDir))
      });
}