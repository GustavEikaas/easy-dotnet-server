using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Workspace;

/// <summary>
/// Verifies that workspace/run:
///   1. Sends a promptSelection reverse request when no default is set.
///   2. Persists the picked project — proven by a second call with UseDefault=true:
///      a. No promptSelection is sent (picker bypassed).
///      b. runCommandManaged receives an identical RunCommand, confirming the same project was run.
/// </summary>
public abstract class WorkspaceRunContainerTests<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{

  private int _selectionCallCount;

  private readonly TaskCompletionSource<TestTrackedJob> _run1Job = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private readonly TaskCompletionSource<TestTrackedJob> _run2Job = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private int _runCommandCallCount;

  protected override void ConfigureRpc(JsonRpc rpc) => rpc.AddLocalRpcTarget(new TestClientHandlers(this), new JsonRpcTargetOptions { DisposeOnDisconnect = false });

  [Fact]
  public async Task Run_WithTwoRunnableProjects_PersistsDefaultAndBypassesPickerOnSecondCall()
  {
    using var solution = new TempContainerSolution();

    await InitializeWorkspaceAsync(solution);

    await Container.Rpc.InvokeWithParameterObjectAsync(
      "workspace/run",
      new { useDefault = false, useLaunchProfile = false, filePath = (string?)null, cliArgs = (string?)null });

    Assert.Equal(1, _selectionCallCount);

    var job1 = await _run1Job.Task.WaitAsync(TimeSpan.FromMinutes(3));


    await Container.Rpc.InvokeWithParameterObjectAsync(
      "workspace/run",
      new { useDefault = true, useLaunchProfile = false, filePath = (string?)null, cliArgs = (string?)null });

    Assert.Equal(1, _selectionCallCount);

    var job2 = await _run2Job.Task.WaitAsync(TimeSpan.FromMinutes(3));

    Assert.Equal(job1.Command.Executable, job2.Command.Executable);
    Assert.Equal(job1.Command.WorkingDirectory, job2.Command.WorkingDirectory);
    Assert.Equal(job1.Command.Arguments, job2.Command.Arguments);
  }

  /// <summary>
  /// Handles server-initiated reverse requests registered via AddLocalRpcTarget so that
  /// JsonRpcMethodAttribute can set UseSingleObjectParameterDeserialization = true (required
  /// when the server uses InvokeWithParameterObjectAsync — named-params object style).
  /// </summary>
  private sealed class TestClientHandlers(WorkspaceRunContainerTests<TContainer> test)
  {
    [JsonRpcMethod("promptSelection", UseSingleObjectParameterDeserialization = true)]
    public Task<string?> PromptSelection(TestPromptSelectionRequest request)
    {
      Interlocked.Increment(ref test._selectionCallCount);
      return Task.FromResult<string?>(request.Choices[0].Id);
    }

    /// <summary>
    /// Capture the run command shape then immediately reject the request.
    /// Rejecting causes SetFailedToStart on the server which releases the LongRunning
    /// slot cleanly — no need to fake the startup hook or call processExited.
    /// </summary>
    [JsonRpcMethod("runCommandManaged", UseSingleObjectParameterDeserialization = true)]
    public Task<object> RunCommandManaged(TestTrackedJob job)
    {
      var callNum = Interlocked.Increment(ref test._runCommandCallCount);
      if (callNum == 1) test._run1Job.TrySetResult(job);
      else if (callNum == 2) test._run2Job.TrySetResult(job);

      return Task.FromException<object>(new InvalidOperationException("Test cancelled run — no process spawning in container tests"));
    }
  }
}

public sealed class WorkspaceRunSdk8Linux : WorkspaceRunContainerTests<Sdk8LinuxContainer>;
public sealed class WorkspaceRunSdk9Linux : WorkspaceRunContainerTests<Sdk9LinuxContainer>;
public sealed class WorkspaceRunSdk10Linux : WorkspaceRunContainerTests<Sdk10LinuxContainer>;