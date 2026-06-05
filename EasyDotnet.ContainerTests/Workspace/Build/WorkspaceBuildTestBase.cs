using System.Text;
using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.IDE.Models.Client;
using Xunit.Sdk;

namespace EasyDotnet.ContainerTests.Workspace.Build;

/// <summary>
/// Abstract base for all workspace/build container tests.
///
/// Uses <see cref="Channel{T}"/> to capture server-initiated reverse requests in order:
///   - <c>promptSelection</c> calls are captured and held until the test calls
///     <see cref="ReceiveSelectionAsync"/>, which supplies the chosen id and unblocks the server.
///   - <c>runCommandManaged</c> calls are captured and acknowledged with a fake success response.
///     After the test reads the job via <see cref="ReceiveRunCommandAsync(int)"/>, a
///     <c>processExited</c> notification is sent with the requested exit code.
///   - Build result notifications are captured for assertions:
///     <c>displayMessage</c>, <c>displayError</c>, <c>quickfix/set</c>, and
///     <c>quickfix/set-silent</c>.
/// </summary>
public abstract class WorkspaceBuildTestBase<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(30);
  private static readonly TimeSpan RunCommandTimeout = TimeSpan.FromMinutes(3);
  private static readonly TimeSpan NotificationTimeout = TimeSpan.FromMinutes(3);
  private static readonly TimeSpan NotificationAfterScopeTimeout = TimeSpan.FromSeconds(15);

  private int _selectionCallCount;

  private readonly Channel<(TestPromptSelectionRequest Request, TaskCompletionSource<string?> Reply)> _selections =
    Channel.CreateUnbounded<(TestPromptSelectionRequest, TaskCompletionSource<string?>)>();

  private readonly Channel<TestTrackedJob> _runCommands =
    Channel.CreateUnbounded<TestTrackedJob>();

  private readonly Channel<string> _displayErrors =
    Channel.CreateUnbounded<string>();

  private readonly Channel<string> _displayMessages =
    Channel.CreateUnbounded<string>();

  private readonly Channel<TestQuickFixItem[]> _quickFixSets =
    Channel.CreateUnbounded<TestQuickFixItem[]>();

  private readonly Channel<TestQuickFixItem[]> _quickFixSetsSilent =
    Channel.CreateUnbounded<TestQuickFixItem[]>();

  /// <summary>Total number of <c>promptSelection</c> calls received from the server so far.</summary>
  protected int SelectionCallCount => Volatile.Read(ref _selectionCallCount);

  /// <summary>
  /// Starts a <c>workspace/build</c> call, registers it as the active RPC scope, and returns the task.
  /// </summary>
  protected Task BeginBuild(
    bool useDefault = false,
    bool useTerminal = false,
    string? buildArgs = null)
    => BeginCall(Container.Rpc.WorkspaceBuildAsync(useDefault, useTerminal, buildArgs));

  /// <summary>
  /// Starts a <c>workspace/build-solution</c> call, registers it as the active RPC scope, and returns the task.
  /// </summary>
  protected Task BeginBuildSolution(
    bool useDefault = false,
    bool useTerminal = false,
    string? buildArgs = null)
    => BeginCall(Container.Rpc.WorkspaceBuildSolutionAsync(useDefault, useTerminal, buildArgs));

  /// <summary>
  /// Waits for the next <c>promptSelection</c> call, invokes <paramref name="respond"/> with
  /// the request to obtain the chosen id (return <c>null</c> to dismiss the picker), unblocks
  /// the server, and returns the full request for assertions.
  /// Races against the active RPC scope — if the scope completes before the selection arrives
  /// the method throws immediately with pending notifications as context.
  /// </summary>
  protected async Task<TestPromptSelectionRequest> ReceiveSelectionAsync(
    Func<TestPromptSelectionRequest, string?> respond)
  {
    var (req, tcs) = await ReceiveAsync(
      _selections.Reader,
      "promptSelection",
      SelectionTimeout,
      CollectPendingNotifications);
    tcs.SetResult(respond(req));
    return req;
  }

  /// <summary>
  /// Waits for the next <c>runCommandManaged</c> call, sends <c>processExited</c> with
  /// <paramref name="exitCode"/> to release the managed terminal slot, and returns the captured job.
  /// Races against the active RPC scope so mismatched code paths fail quickly.
  /// </summary>
  protected async Task<TestTrackedJob> ReceiveRunCommandAsync(int exitCode = 0)
  {
    var job = await ReceiveAsync(
      _runCommands.Reader,
      "runCommandManaged",
      RunCommandTimeout,
      CollectPendingNotifications);
    return await CompleteJobAsync(job, exitCode);
  }

  /// <summary>Waits for the next <c>displayError</c> notification and returns the message.</summary>
  protected async Task<string> ReceiveDisplayErrorAsync() =>
    await ReceiveNotificationAsync(_displayErrors.Reader, "displayError");

  /// <summary>Waits for the next <c>displayMessage</c> notification and returns the message.</summary>
  protected async Task<string> ReceiveDisplayMessageAsync() =>
    await ReceiveNotificationAsync(_displayMessages.Reader, "displayMessage");

  /// <summary>Waits for the next <c>quickfix/set</c> notification and returns all items.</summary>
  protected async Task<TestQuickFixItem[]> ReceiveQuickFixSetAsync() =>
    await ReceiveNotificationAsync(_quickFixSets.Reader, "quickfix/set");

  /// <summary>Waits for the next <c>quickfix/set-silent</c> notification and returns all items.</summary>
  protected async Task<TestQuickFixItem[]> ReceiveQuickFixSetSilentAsync() =>
    await ReceiveNotificationAsync(_quickFixSetsSilent.Reader, "quickfix/set-silent");

  /// <summary>
  /// Returns true if no <c>runCommandManaged</c> call is already queued in the channel.
  /// Call this only after the active RPC scope has completed.
  /// </summary>
  protected bool RunCommandNotReceived() =>
    IsNotReceived(_runCommands.Reader);

  /// <summary>
  /// Returns true if no <c>quickfix/set</c> notification is queued in the channel.
  /// Call this only after the active RPC scope has completed.
  /// </summary>
  protected bool QuickFixSetNotReceived() =>
    IsNotReceived(_quickFixSets.Reader);

  /// <summary>
  /// Returns true if no <c>quickfix/set-silent</c> notification is queued in the channel.
  /// Call this only after the active RPC scope has completed.
  /// </summary>
  protected bool QuickFixSetSilentNotReceived() =>
    IsNotReceived(_quickFixSetsSilent.Reader);

  private async Task<TestTrackedJob> CompleteJobAsync(TestTrackedJob job, int exitCode)
  {
    await Container.Rpc.NotifyWithParameterObjectAsync("processExited", new { job.JobId, exitCode });
    return job;
  }

  private async Task<T> ReceiveNotificationAsync<T>(ChannelReader<T> reader, string notificationName)
    => await ReceiveAsync(
      reader,
      notificationName,
      NotificationTimeout,
      CollectPendingNotifications,
      NotificationAfterScopeTimeout);

  private string CollectPendingNotifications()
  {
    var sb = new StringBuilder();

    while (_displayErrors.Reader.TryRead(out var err))
      sb.Append($"\n  displayError: \"{err}\"");
    while (_displayMessages.Reader.TryRead(out var msg))
      sb.Append($"\n  displayMessage: \"{msg}\"");
    while (_quickFixSets.Reader.TryRead(out var items))
      sb.Append($"\n  quickfix/set: {items.Length} item(s)");
    while (_quickFixSetsSilent.Reader.TryRead(out var items))
      sb.Append($"\n  quickfix/set-silent: {items.Length} item(s)");

    return sb.Length > 0 ? $"\n Pending notifications:{sb}" : string.Empty;
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

  public override void DisplayMessage(TestDisplayMessage message) =>
    _displayMessages.Writer.TryWrite(message.Message);

  public override void SetQuickFix(TestQuickFixItem[] quickFixItems) =>
    _quickFixSets.Writer.TryWrite(quickFixItems);

  public override void SetQuickFixSilent(TestQuickFixItem[] quickFixItems) =>
    _quickFixSetsSilent.Writer.TryWrite(quickFixItems);
}