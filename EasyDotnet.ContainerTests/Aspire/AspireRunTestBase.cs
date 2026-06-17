using System.Text;
using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Workspace.Run;
using EasyDotnet.IDE.Models.Client;
using StreamJsonRpc;
using Xunit.Sdk;

namespace EasyDotnet.ContainerTests.Aspire;

/// <summary>
/// Base for Aspire DCP container tests. Running an AppHost project via <c>workspace/run</c>
/// routes through the Aspire host: it stands up the DCP server and then asks the editor to run
/// the AppHost (with the <c>DEBUG_SESSION_*</c> env injected). That AppHost launch is dispatched
/// as a fire-and-forget <c>runCommandManaged</c> reverse request <em>after</em> <c>workspace/run</c>
/// returns, so it is captured on its own channel with a generous timeout (not raced against the
/// RPC scope like the plain workspace/run tests).
/// </summary>
public abstract class AspireRunTestBase<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private static readonly TimeSpan RunCommandTimeout = TimeSpan.FromMinutes(3);

  private readonly Channel<TestTrackedJob> _runCommands = Channel.CreateUnbounded<TestTrackedJob>();
  private readonly Channel<string> _displayErrors = Channel.CreateUnbounded<string>();

  protected override void ConfigureRpc(JsonRpc rpc) =>
    rpc.AddLocalRpcTarget(new RpcHandlers(this), new JsonRpcTargetOptions { DisposeOnDisconnect = false });

  /// <summary>Triggers <c>workspace/run</c> (auto-selects when the workspace has one runnable project).</summary>
  protected Task RunAsync(bool useDefault = false) =>
    Container.Rpc.WorkspaceRunAsync(useDefault: useDefault);

  /// <summary>
  /// Waits for the next <c>runCommandManaged</c> reverse request, acknowledges its exit so the
  /// terminal slot is released, and returns the captured job.
  /// </summary>
  protected async Task<TestTrackedJob> ReceiveRunCommandAsync()
  {
    using var cts = new CancellationTokenSource(RunCommandTimeout);
    TestTrackedJob job;
    try
    {
      job = await _runCommands.Reader.ReadAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
      throw new XunitException($"runCommandManaged not received within {RunCommandTimeout.TotalSeconds:0}s.{CollectPendingErrors()}");
    }

    await Container.Rpc.NotifyWithParameterObjectAsync("processExited", new { job.JobId, exitCode = 0 });
    return job;
  }

  private string CollectPendingErrors()
  {
    var sb = new StringBuilder();
    while (_displayErrors.Reader.TryRead(out var err))
      sb.Append($"\n  displayError: \"{err}\"");
    return sb.Length > 0 ? $"\n Pending errors:{sb}" : string.Empty;
  }

  private sealed class RpcHandlers(AspireRunTestBase<TContainer> test)
  {
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