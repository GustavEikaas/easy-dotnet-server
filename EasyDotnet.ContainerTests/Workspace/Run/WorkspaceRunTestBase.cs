using System.Text;
using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.IDE.Models.Client;
using StreamJsonRpc;
using Xunit.Sdk;

namespace EasyDotnet.ContainerTests.Workspace.Run;

/// <summary>
/// Abstract base for all workspace/run container tests.
///
/// Uses <see cref="Channel{T}"/> to capture server-initiated reverse requests in order:
///   - <c>promptSelection</c> calls are captured and held until the test calls
///     <see cref="ReceiveSelectionAsync"/>, which supplies the chosen id and unblocks the server.
///   - <c>runCommandManaged</c> calls are captured and acknowledged with a fake success response.
///     After the test reads the job via <see cref="ReceiveRunCommandAsync"/>, a <c>processExited</c>
///     notification is automatically sent to the server to release the terminal slot.
///
/// <para>
/// Architecture note — two distinct patterns for reverse requests:
///
/// Both <c>promptSelection</c> and <c>runCommandManaged</c> are now dispatched within the
/// <c>workspace/run</c> scope (i.e. before <c>workspace/run</c> returns). This means
/// <see cref="ReceiveSelectionAsync"/> and <see cref="ReceiveRunCommandAsync"/> both race
/// against the scope task: if the scope completes before the expected reverse request arrives,
/// something went wrong and both methods throw immediately with pending <c>displayError</c>
/// messages as context.
/// </para>
///
/// <para>
/// IMPORTANT — test pattern for workspace/run with pickers:
/// Use <see cref="BeginRun"/> instead of calling WorkspaceRunAsync directly — this stores the
/// task as the active RPC scope so that <see cref="ReceiveSelectionAsync"/> and
/// <see cref="ReceiveRunCommandAsync"/> can race against it.
/// <code>
///   var runTask = BeginRun(useLaunchProfile: true);
///   await ReceiveSelectionAsync(req => req.Choices[0].Id);
///   await runTask;
///   var job = await ReceiveRunCommandAsync();
/// </code>
/// </para>
/// </summary>
public abstract class WorkspaceRunTestBase<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(30);
  private static readonly TimeSpan RunCommandTimeout = TimeSpan.FromMinutes(3);

  private int _selectionCallCount;

  private readonly Channel<(TestPromptSelectionRequest Request, TaskCompletionSource<string?> Reply)> _selections =
    Channel.CreateUnbounded<(TestPromptSelectionRequest, TaskCompletionSource<string?>)>();

  private readonly Channel<TestTrackedJob> _runCommands =
    Channel.CreateUnbounded<TestTrackedJob>();

  private readonly Channel<string> _displayErrors =
    Channel.CreateUnbounded<string>();

  /// <summary>Total number of <c>promptSelection</c> calls received from the server so far.</summary>
  protected int SelectionCallCount => Volatile.Read(ref _selectionCallCount);

  protected override void ConfigureRpc(JsonRpc rpc) =>
    rpc.AddLocalRpcTarget(new RpcHandlers(this), new JsonRpcTargetOptions { DisposeOnDisconnect = false });

  /// <summary>
  /// Starts a <c>workspace/run</c> call, registers it as the active RPC scope, and returns the
  /// task. All <c>ReceiveXxx</c> helpers race against this task — if it completes before the
  /// expected reverse request arrives they throw immediately instead of waiting for their timeout.
  /// </summary>
  protected Task BeginRun(
    bool useDefault = false,
    bool useLaunchProfile = false,
    string? filePath = null,
    string? cliArgs = null)
    => BeginCall(Container.Rpc.WorkspaceRunAsync(useDefault, useLaunchProfile, filePath, cliArgs));

  /// <summary>
  /// Waits for the next <c>promptSelection</c> call, invokes <paramref name="respond"/> with
  /// the request to obtain the chosen id (return <c>null</c> to dismiss the picker), unblocks
  /// the server, and returns the full request for assertions.
  /// Races against the active RPC scope — if the scope completes before the selection arrives
  /// the method throws immediately with any pending <c>displayError</c> messages as context.
  /// </summary>
  protected async Task<TestPromptSelectionRequest> ReceiveSelectionAsync(
    Func<TestPromptSelectionRequest, string?> respond)
  {
    var readTask = _selections.Reader.ReadAsync().AsTask();
    var scope = _rpcScope;

    if (scope is not null)
    {
      var winner = await Task.WhenAny(readTask, scope);
      if (winner == scope)
      {
        await scope; // surface any server-side exception first
        throw new XunitException(
          $"RPC scope completed before expected promptSelection arrived.{CollectPendingErrors()}");
      }
    }
    else
    {
      await readTask.WaitAsync(SelectionTimeout);
    }

    var (req, tcs) = await readTask;
    tcs.SetResult(respond(req));
    return req;
  }

  /// <summary>
  /// Waits for the next <c>runCommandManaged</c> call and returns the captured job.
  /// The server has already been told to fail so no real process is started.
  /// Now that <c>workspace/run</c> dispatches <c>runCommandManaged</c> before returning,
  /// the RPC scope races are fully applicable: if the scope completes without the item
  /// being in the channel, the call-path took a different route (e.g. build failed or
  /// picker was dismissed) and we fail immediately instead of waiting.
  /// </summary>
  protected async Task<TestTrackedJob> ReceiveRunCommandAsync()
  {
    var scope = _rpcScope;

    if (scope is not null)
    {
      // Fast path: scope already complete. The item must already be in the channel
      // (server fix guarantees runCommandManaged is dispatched before workspace/run returns).
      // Use TryRead directly — do NOT start ReadAsync first, as it would eagerly consume
      // the item from the channel and leave TryRead with nothing to read.
      if (scope.IsCompleted)
      {
        if (_runCommands.Reader.TryRead(out var immediate))
          return await CompleteJobAsync(immediate);
        await scope; // surface any server-side exception first
        throw new XunitException(
          $"RPC scope completed without sending runCommandManaged.{CollectPendingErrors()}");
      }

      // Scope still running: race the channel read against the scope completing.
      var readTask = _runCommands.Reader.ReadAsync().AsTask();
      var winner = await Task.WhenAny(readTask, scope);
      if (winner == readTask)
        return await CompleteJobAsync(await readTask);

      // Scope won — item may have arrived at the same instant; try one last TryRead.
      if (_runCommands.Reader.TryRead(out var buffered))
        return await CompleteJobAsync(buffered);
      await scope;
      throw new XunitException(
        $"RPC scope completed without sending runCommandManaged.{CollectPendingErrors()}");
    }

    try
    {
      var job = await _runCommands.Reader.ReadAsync().AsTask().WaitAsync(RunCommandTimeout);
      return await CompleteJobAsync(job);
    }
    catch (TimeoutException)
    {
      throw new XunitException(
        $"runCommandManaged not received within {RunCommandTimeout.TotalMinutes:0} minutes.{CollectPendingErrors()}");
    }
  }

  /// <summary>
  /// Signals the server that the tracked job's process has exited, releasing the terminal slot
  /// so subsequent runs in the same test can claim it. Returns the job unchanged.
  /// </summary>
  private async Task<TestTrackedJob> CompleteJobAsync(TestTrackedJob job)
  {
    await Container.Rpc.NotifyWithParameterObjectAsync("processExited", new { job.JobId, exitCode = 0 });
    return job;
  }

  /// <summary>
  /// Waits for the next <c>displayError</c> notification and returns the message.
  /// </summary>
  protected async Task<string> ReceiveDisplayErrorAsync() =>
    await _displayErrors.Reader.ReadAsync().AsTask().WaitAsync(SelectionTimeout);

  /// <summary>
  /// Returns true if no <c>runCommandManaged</c> call is already queued in the channel.
  /// Call this only after the <c>workspace/run</c> RPC task has completed — at that point
  /// the server is done and no further reverse requests will arrive.
  /// </summary>
  protected bool RunCommandNotReceived() =>
    !_runCommands.Reader.TryRead(out _);

  /// <summary>
  /// Drains any pending <c>displayError</c> messages and formats them as a context suffix
  /// for <c>XunitException</c> messages thrown when the RPC scope ends unexpectedly.
  /// </summary>
  private string CollectPendingErrors()
  {
    var sb = new StringBuilder();
    while (_displayErrors.Reader.TryRead(out var err))
      sb.Append($"\n  displayError: \"{err}\"");
    return sb.Length > 0 ? $"\n Pending errors:{sb}" : string.Empty;
  }

  private sealed class RpcHandlers(WorkspaceRunTestBase<TContainer> test)
  {
    [JsonRpcMethod("promptSelection", UseSingleObjectParameterDeserialization = true)]
    public async Task<string?> PromptSelection(TestPromptSelectionRequest request)
    {
      Interlocked.Increment(ref test._selectionCallCount);
      var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
      await test._selections.Writer.WriteAsync((request, tcs));
      return await tcs.Task;
    }

    /// <summary>
    /// Captures the job then returns a successful response.
    /// The test infrastructure sends <c>processExited</c> automatically via
    /// <see cref="WorkspaceRunTestBase{TContainer}.ReceiveRunCommandAsync"/> to release the
    /// terminal slot so subsequent runs in the same test can proceed.
    /// </summary>
    [JsonRpcMethod("runCommandManaged", UseSingleObjectParameterDeserialization = true)]
    public Task<RunCommandResponse> RunCommandManaged(TestTrackedJob job)
    {
      test._runCommands.Writer.TryWrite(job);
      return Task.FromResult(new RunCommandResponse(0));
    }

    [JsonRpcMethod("displayError", UseSingleObjectParameterDeserialization = true)]
    public void DisplayError(TestDisplayMessage message) =>
      test._displayErrors.Writer.TryWrite(message.Message);
  }
}