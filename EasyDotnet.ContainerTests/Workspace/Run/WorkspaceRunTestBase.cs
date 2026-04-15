using System.Text;
using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using StreamJsonRpc;
using Xunit.Sdk;

namespace EasyDotnet.ContainerTests.Workspace.Run;

/// <summary>
/// Abstract base for all workspace/run container tests.
///
/// Uses <see cref="Channel{T}"/> to capture server-initiated reverse requests in order:
///   - <c>promptSelection</c> calls are captured and held until the test calls
///     <see cref="ReceiveSelectionAsync"/>, which supplies the chosen id and unblocks the server.
///   - <c>runCommandManaged</c> calls are captured and immediately rejected so the server
///     releases the LongRunning slot cleanly without spawning a real process.
///
/// <para>
/// IMPORTANT — test pattern for workspace/run with pickers:
/// Do NOT await the workspace/run RPC task before calling <see cref="ReceiveSelectionAsync"/>.
/// Use <see cref="BeginRun"/> instead of calling WorkspaceRunAsync directly — this stores the
/// task as the active RPC scope so that all <c>ReceiveXxx</c> helpers can race against it and
/// fail immediately (with context) instead of waiting for their full timeout.
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
  /// Races against the active RPC scope — if the scope is already complete (or completes
  /// before the command arrives) and nothing is in the channel, throws immediately instead
  /// of waiting for the full <see cref="RunCommandTimeout"/>.
  /// </summary>
  protected async Task<TestTrackedJob> ReceiveRunCommandAsync()
  {
    var scope = _rpcScope;

    if (scope?.IsCompleted == true)
    {
      if (_runCommands.Reader.TryRead(out var immediate))
        return immediate;
      throw new XunitException(
        $"RPC scope completed without sending runCommandManaged.{CollectPendingErrors()}");
    }

    var readTask = _runCommands.Reader.ReadAsync().AsTask();

    if (scope is not null)
    {
      var winner = await Task.WhenAny(readTask, scope);
      if (winner == scope)
      {
        if (_runCommands.Reader.TryRead(out var raced))
          return raced;
        await scope;
        throw new XunitException(
          $"RPC scope completed without sending runCommandManaged.{CollectPendingErrors()}");
      }
    }
    else
    {
      await readTask.WaitAsync(RunCommandTimeout);
    }

    return await readTask;
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
    /// Captures the job then rejects the request — same pattern used across all run tests.
    /// Rejecting causes SetFailedToStart on the server which releases the LongRunning slot cleanly.
    /// </summary>
    [JsonRpcMethod("runCommandManaged", UseSingleObjectParameterDeserialization = true)]
    public Task<object> RunCommandManaged(TestTrackedJob job)
    {
      test._runCommands.Writer.TryWrite(job);
      return Task.FromException<object>(
        new InvalidOperationException("Test cancelled run — no process spawning in container tests"));
    }

    [JsonRpcMethod("displayError", UseSingleObjectParameterDeserialization = true)]
    public void DisplayError(TestDisplayMessage message) =>
      test._displayErrors.Writer.TryWrite(message.Message);
  }
}
