using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using StreamJsonRpc;

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
/// The server's <c>promptSelection</c> reverse request arrives while workspace/run is still
/// processing. Awaiting workspace/run first would deadlock because nobody is reading the
/// selection channel. Start the task, handle all expected selections, THEN await the task:
/// <code>
///   var runTask = Container.Rpc.WorkspaceRunAsync(useLaunchProfile: true);
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
  /// Waits for the next <c>promptSelection</c> call, invokes <paramref name="respond"/> with
  /// the request to obtain the chosen id (return <c>null</c> to dismiss the picker), unblocks
  /// the server, and returns the full request for assertions.
  /// </summary>
  protected async Task<TestPromptSelectionRequest> ReceiveSelectionAsync(
    Func<TestPromptSelectionRequest, string?> respond)
  {
    var (req, tcs) = await _selections.Reader.ReadAsync().AsTask().WaitAsync(SelectionTimeout);
    tcs.SetResult(respond(req));
    return req;
  }

  /// <summary>
  /// Waits for the next <c>runCommandManaged</c> call and returns the captured job.
  /// The server has already been told to fail so no real process is started.
  /// </summary>
  protected async Task<TestTrackedJob> ReceiveRunCommandAsync() =>
    await _runCommands.Reader.ReadAsync().AsTask().WaitAsync(RunCommandTimeout);

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