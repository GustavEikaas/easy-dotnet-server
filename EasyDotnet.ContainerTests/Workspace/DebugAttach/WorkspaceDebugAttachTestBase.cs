using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using EasyDotnet.ContainerTests.Workspace.Run;
using EasyDotnet.IDE.Models.Client;
using StreamJsonRpc;
using Xunit.Sdk;

namespace EasyDotnet.ContainerTests.Workspace.DebugAttach;

/// <summary>
/// Abstract base for workspace/debug-attach container tests.
///
/// Handles two phases:
///
/// Phase 1 — run: <c>runCommandManaged</c> is captured, a background task connects to the
/// startup hook pipe (<c>EASY_DOTNET_HOOK_PIPE</c>), writes a fake PID (<see cref="FakePid"/>)
/// and reads the resume byte. <see cref="ReceiveRunJobAsync"/> waits until that pipe exchange
/// completes, guaranteeing the server's <c>OnPidReceived</c> callback has fired and the
/// <c>RunningProcessRegistry</c> is populated before the test proceeds.
///
/// Phase 2 — debug-attach: <c>picker/pick</c> reverse requests are captured and held until
/// the test calls <see cref="ReceivePickerAsync"/>, which supplies the response ids (or
/// <c>null</c> to dismiss) and unblocks the server.
///
/// <para>
/// IMPORTANT — test pattern:
/// <code>
///   await BeginRun();
///   var job = await ReceiveRunJobAsync();   // PID injected; registry populated
///
///   var debugTask = BeginDebugAttach();
///   var picker = await ReceivePickerAsync(_ =&gt; null); // dismiss
///   await debugTask;
/// </code>
/// </para>
/// </summary>
public abstract class WorkspaceDebugAttachTestBase<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  protected const int FakePid = 12345;

  private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(3);
  private static readonly TimeSpan PickerTimeout = TimeSpan.FromSeconds(30);

  private Task? _pidInjectionTask;

  private readonly Channel<TestTrackedJob> _runJobs =
    Channel.CreateUnbounded<TestTrackedJob>();

  private readonly Channel<(TestPickerRequest Request, TaskCompletionSource<string[]?> Reply)> _pickers =
    Channel.CreateUnbounded<(TestPickerRequest, TaskCompletionSource<string[]?>)>();

  private readonly Channel<string> _displayErrors =
    Channel.CreateUnbounded<string>();

  protected override void ConfigureRpc(JsonRpc rpc) =>
    rpc.AddLocalRpcTarget(new RpcHandlers(this), new JsonRpcTargetOptions { DisposeOnDisconnect = false });

  /// <summary>
  /// Starts a <c>workspace/run</c> call and awaits its completion.
  /// <c>runCommandManaged</c> is dispatched and acknowledged before <c>workspace/run</c> returns,
  /// so <see cref="ReceiveRunJobAsync"/> can be called immediately afterwards.
  /// </summary>
  protected Task BeginRun(bool useDefault = false) =>
    BeginCall(Container.Rpc.WorkspaceRunAsync(useDefault));

  /// <summary>
  /// Starts a <c>workspace/debug-attach</c> call, registers it as the active RPC scope, and
  /// returns the task. <see cref="ReceivePickerAsync"/> races against this scope.
  /// </summary>
  protected Task BeginDebugAttach() =>
    BeginCall(Container.Rpc.WorkspaceDebugAttachAsync());

  /// <summary>
  /// Waits for the next <c>runCommandManaged</c> call, then waits for the startup hook pipe
  /// exchange (fake PID written, resume byte consumed) to complete. Returns only once the
  /// server's <c>OnPidReceived</c> has fired and the registry entry exists.
  /// </summary>
  protected async Task<TestTrackedJob> ReceiveRunJobAsync()
  {
    var job = await _runJobs.Reader.ReadAsync().AsTask().WaitAsync(RunTimeout);
    if (_pidInjectionTask is not null)
      await _pidInjectionTask;
    return job;
  }

  /// <summary>
  /// Signals the server that the tracked job's process has exited, triggering registry cleanup.
  /// After calling this, wait briefly before calling <see cref="BeginDebugAttach"/> so the
  /// server's background unregister task has time to run.
  /// </summary>
  protected Task CompleteJobAsync(TestTrackedJob job) =>
    Container.Rpc.NotifyWithParameterObjectAsync("processExited", new { job.JobId, exitCode = 0 });

  /// <summary>
  /// Waits for the next <c>picker/pick</c> reverse request, invokes <paramref name="respond"/>
  /// with the request to obtain the selected ids (return <c>null</c> to dismiss), unblocks the
  /// server, and returns the full request for assertions.
  /// Races against the active RPC scope — if the scope completes before the picker arrives the
  /// method throws immediately with any pending <c>displayError</c> messages as context.
  /// </summary>
  protected async Task<TestPickerRequest> ReceivePickerAsync(Func<TestPickerRequest, string[]?> respond)
  {
    var readTask = _pickers.Reader.ReadAsync().AsTask();
    var scope = _rpcScope;

    if (scope is not null)
    {
      var timeout = Task.Delay(PickerTimeout);
      var winner = await Task.WhenAny(readTask, scope, timeout);
      if (winner == timeout)
        throw new XunitException(
          $"Timed out after {PickerTimeout.TotalSeconds:0}s waiting for picker/pick.{CollectPendingErrors()}");
      if (winner == scope)
      {
        await scope; // surface any server-side exception first
        throw new XunitException(
          $"RPC scope completed before expected picker/pick arrived.{CollectPendingErrors()}");
      }
    }
    else
    {
      await readTask.WaitAsync(PickerTimeout);
    }

    var (req, tcs) = await readTask;
    tcs.SetResult(respond(req));
    return req;
  }

  /// <summary>Waits for the next <c>displayError</c> notification and returns the message.</summary>
  protected async Task<string> ReceiveDisplayErrorAsync() =>
    await _displayErrors.Reader.ReadAsync().AsTask().WaitAsync(PickerTimeout);

  /// <summary>
  /// Races between a <c>picker/pick</c> reverse request and a <c>displayError</c> notification.
  /// Returns whichever arrives first. If a picker arrives, <paramref name="respond"/> is invoked
  /// to supply the selection (return <c>null</c> to dismiss) and the picker is returned as
  /// <c>Picker</c>; if an error arrives it is returned as <c>Error</c>. Races against the active
  /// RPC scope — if the scope completes before either arrives the method throws immediately.
  /// Use this instead of <see cref="ReceiveDisplayErrorAsync"/> whenever external .NET processes
  /// may exist in the environment and the server might show a picker rather than an error.
  /// </summary>
  protected async Task<(TestPickerRequest? Picker, string? Error)> ReceivePickerOrErrorAsync(
    Func<TestPickerRequest, string[]?> respond)
  {
    var pickerTask = _pickers.Reader.ReadAsync().AsTask();
    var errorTask = _displayErrors.Reader.ReadAsync().AsTask();
    var scope = _rpcScope;
    var timeoutTask = Task.Delay(PickerTimeout);

    var winner = scope is not null
      ? await Task.WhenAny(pickerTask, errorTask, scope, timeoutTask)
      : await Task.WhenAny(pickerTask, errorTask, timeoutTask);

    if (winner == timeoutTask)
      throw new XunitException(
        $"Timed out after {PickerTimeout.TotalSeconds:0}s waiting for picker/pick or displayError.{CollectPendingErrors()}");

    if (winner == scope)
    {
      await scope;
      // Scope may complete at the same instant as delivery — do one final check.
      if (pickerTask.IsCompletedSuccessfully)
      {
        var (req, tcs) = pickerTask.Result;
        tcs.SetResult(respond(req));
        return (req, null);
      }
      if (errorTask.IsCompletedSuccessfully)
        return (null, errorTask.Result);
      throw new XunitException(
        $"RPC scope completed before picker/pick or displayError arrived.{CollectPendingErrors()}");
    }

    if (winner == pickerTask)
    {
      var (req, tcs) = await pickerTask;
      tcs.SetResult(respond(req));
      return (req, null);
    }

    return (null, await errorTask);
  }

  private string CollectPendingErrors()
  {
    var sb = new StringBuilder();
    while (_displayErrors.Reader.TryRead(out var err))
      sb.Append($"\n  displayError: \"{err}\"");
    return sb.Length > 0 ? $"\nPending errors:{sb}" : string.Empty;
  }

  private sealed class RpcHandlers(WorkspaceDebugAttachTestBase<TContainer> test)
  {
    /// <summary>
    /// Captures the job, fires a background task to inject the fake PID via the startup hook
    /// pipe, and returns immediately so the server can proceed to start its pipe listener.
    /// </summary>
    [JsonRpcMethod("runCommandManaged", UseSingleObjectParameterDeserialization = true)]
    public Task<RunCommandResponse> RunCommandManaged(TestTrackedJob job)
    {
      test._runJobs.Writer.TryWrite(job);

      if (job.Command.EnvironmentVariables.TryGetValue("EASY_DOTNET_HOOK_PIPE", out var pipeName))
        test._pidInjectionTask = InjectPidAsync(pipeName, FakePid);

      return Task.FromResult(new RunCommandResponse(0));
    }

    [JsonRpcMethod("picker/pick", UseSingleObjectParameterDeserialization = true)]
    public async Task<TestPickerResult?> PickerPick(TestPickerRequest request)
    {
      var tcs = new TaskCompletionSource<string[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
      await test._pickers.Writer.WriteAsync((request, tcs));
      var selectedIds = await tcs.Task;
      return selectedIds is null ? null : new TestPickerResult(selectedIds);
    }

    [JsonRpcMethod("displayError", UseSingleObjectParameterDeserialization = true)]
    public void DisplayError(TestDisplayMessage message) =>
      test._displayErrors.Writer.TryWrite(message.Message);

    /// <summary>
    /// Connects to the startup hook named pipe, writes the fake PID as a little-endian int32,
    /// and reads the single resume byte the server sends after calling <c>session.Resume()</c>.
    /// The pipe is created by <c>StartupHookService.CreateSession</c> (a <c>NamedPipeServerStream</c>
    /// at <c>/tmp/CoreFxPipe_&lt;name&gt;</c>), accessible from the host because <c>/tmp</c>
    /// is bind-mounted into the test container.
    /// </summary>
    private static async Task InjectPidAsync(string pipeName, int pid)
    {
      using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
      // ConnectAsync blocks until the server calls WaitForConnectionAsync (started in the
      // background task that runs after runCommandManaged returns).
      await pipe.ConnectAsync(10_000);
      await pipe.WriteAsync(BitConverter.GetBytes(pid));
      await pipe.FlushAsync();
      _ = pipe.ReadByte(); // consume the resume byte written by session.Resume()
    }
  }
}