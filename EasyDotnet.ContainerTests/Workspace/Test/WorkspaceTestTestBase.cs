using System.Text;
using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.IDE.Models.Client;
using Xunit.Sdk;

namespace EasyDotnet.ContainerTests.Workspace.Test;

/// <summary>
/// Abstract base for all workspace/test container tests.
///
/// Uses <see cref="Channel{T}"/> to capture server-initiated reverse requests in order:
///   - <c>promptSelection</c> calls are captured and held until the test calls
///     <see cref="ReceiveSelectionAsync"/>, which supplies the chosen id and unblocks the server.
///   - <c>runCommandManaged</c> calls are captured and acknowledged with a fake success response.
///     After the test reads the job via <see cref="ReceiveRunCommandAsync"/>, a <c>processExited</c>
///     notification is automatically sent to the server to release the terminal slot.
///
/// <para>
/// Unlike <c>workspace/run</c>, the <c>workspace/test</c> RPC call does NOT return before
/// <c>runCommandManaged</c> is dispatched — the server awaits the full run inside the handler.
/// This means the RPC scope task will not complete until after <see cref="ReceiveRunCommandAsync"/>
/// sends <c>processExited</c>. The correct test pattern is therefore:
/// <code>
///   var testTask = BeginTest();
///   await ReceiveSelectionAsync(req => req.Choices[0].Id);
///   var job = await ReceiveRunCommandAsync();   // sends processExited, unblocks testTask
///   await testTask;
/// </code>
/// </para>
/// </summary>
public abstract class WorkspaceTestTestBase<TContainer> : ContainerTestBase<TContainer>
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

  /// <summary>
  /// Starts a <c>workspace/test</c> call, registers it as the active RPC scope, and returns the task.
  /// </summary>
  protected Task BeginTest(bool useDefault = false, string? testArgs = null)
    => BeginCall(Container.Rpc.WorkspaceTestAsync(useDefault, testArgs));

  /// <summary>
  /// Starts a <c>workspace/test-solution</c> call, registers it as the active RPC scope, and returns the task.
  /// </summary>
  protected Task BeginTestSolution(bool useDefault = false, string? testArgs = null)
    => BeginCall(Container.Rpc.WorkspaceTestSolutionAsync(useDefault, testArgs));

  /// <summary>
  /// Waits for the next <c>promptSelection</c> call, invokes <paramref name="respond"/> with
  /// the request to obtain the chosen id (return <c>null</c> to dismiss the picker), unblocks
  /// the server, and returns the full request for assertions.
  /// </summary>
  protected async Task<TestPromptSelectionRequest> ReceiveSelectionAsync(
    Func<TestPromptSelectionRequest, string?> respond)
  {
    var (req, tcs) = await ReceiveAsync(
      _selections.Reader,
      "promptSelection",
      SelectionTimeout,
      CollectPendingErrors);
    tcs.SetResult(respond(req));
    return req;
  }

  /// <summary>
  /// Waits for the next <c>runCommandManaged</c> call, automatically sends <c>processExited</c>
  /// to unblock the server, and returns the captured job for assertions.
  /// </summary>
  protected async Task<TestTrackedJob> ReceiveRunCommandAsync()
  {
    var job = await ReceiveAsync(
      _runCommands.Reader,
      "runCommandManaged",
      RunCommandTimeout,
      CollectPendingErrors);

    await Container.Rpc.NotifyWithParameterObjectAsync("processExited", new { job.JobId, exitCode = 0 });
    return job;
  }

  /// <summary>
  /// Waits for the next <c>displayError</c> notification and returns the message.
  /// </summary>
  protected async Task<string> ReceiveDisplayErrorAsync() =>
    await ReceiveAsync(
      _displayErrors.Reader,
      "displayError",
      SelectionTimeout,
      CollectPendingErrors);

  /// <summary>
  /// Returns true if no <c>runCommandManaged</c> call is already queued in the channel.
  /// Call this only after the <c>workspace/test</c> RPC task has completed.
  /// </summary>
  protected bool RunCommandNotReceived() =>
    IsNotReceived(_runCommands.Reader);

  private string CollectPendingErrors()
  {
    var sb = new StringBuilder();
    while (_displayErrors.Reader.TryRead(out var err))
      sb.Append($"\n  displayError: \"{err}\"");
    return sb.Length > 0 ? $"\n Pending errors:{sb}" : string.Empty;
  }

  public override async Task<string?> PromptSelectionAsync(TestPromptSelectionRequest request)
  {
    Interlocked.Increment(ref _selectionCallCount);
    var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    await _selections.Writer.WriteAsync((request, tcs));
    return await tcs.Task;
  }

  public override Task<RunCommandResponse> RunCommandManagedAsync(TestTrackedJob job)
  {
    _runCommands.Writer.TryWrite(job);
    return Task.FromResult(new RunCommandResponse(0));
  }

  public override void DisplayError(TestDisplayMessage message) =>
    _displayErrors.Writer.TryWrite(message.Message);
}
