using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Workspace;

/// <summary>
/// Verifies that a standalone .cs file passed as filePath — when it lives outside any project
/// directory — appears in the workspace/run picker alongside the solution's runnable projects.
/// The picker should contain one entry per runnable project plus one for the standalone file.
/// </summary>
public abstract class WorkspaceRunStandaloneFileTests<TContainer> : ContainerTestBase<TContainer> where TContainer : ServerContainer, new()
{
  private readonly TaskCompletionSource<TestPromptSelectionRequest> _selectionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

  protected override void ConfigureRpc(JsonRpc rpc) => rpc.AddLocalRpcTarget(new TestClientHandlers(this), new JsonRpcTargetOptions { DisposeOnDisconnect = false });

  [Fact]
  public async Task Run_WithStandaloneFileOutsideProject_IncludesFileOptionAlongsideRunnableProjects()
  {
    using var solution = new TempContainerSolution();

    await InitializeWorkspaceAsync(solution);

    await Container.Rpc.InvokeWithParameterObjectAsync("workspace/run",
      new { useDefault = false, useLaunchProfile = false, filePath = solution.StandaloneFilePath, cliArgs = (string?)null });

    var selection = await _selectionTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Contains(selection.Choices, c => c.Display.Contains("ProjectAlpha"));
    Assert.Contains(selection.Choices, c => c.Display.Contains("ProjectBeta"));

    if (Container.SdkMajorVersion >= 10)
    {
      Assert.Equal(3, selection.Choices.Length);
      Assert.Contains(selection.Choices, c => c.Id == "__singlefile__");
    }
    else
    {
      Assert.Equal(2, selection.Choices.Length);
      Assert.DoesNotContain(selection.Choices, c => c.Id == "__singlefile__");
    }
  }

  private sealed class TestClientHandlers(WorkspaceRunStandaloneFileTests<TContainer> test)
  {
    [JsonRpcMethod("promptSelection", UseSingleObjectParameterDeserialization = true)]
    public Task<string?> PromptSelection(TestPromptSelectionRequest request)
    {
      test._selectionTcs.TrySetResult(request);
      return Task.FromResult<string?>(null);
    }

    [JsonRpcMethod("runCommandManaged", UseSingleObjectParameterDeserialization = true)]
    public Task<object> RunCommandManaged(TestTrackedJob _) =>
      Task.FromException<object>(new InvalidOperationException("Test cancelled run — no process spawning in container tests"));
  }
}

public sealed class WorkspaceRunStandaloneFileSdk8Linux : WorkspaceRunStandaloneFileTests<Sdk8LinuxContainer>;
public sealed class WorkspaceRunStandaloneFileSdk9Linux : WorkspaceRunStandaloneFileTests<Sdk9LinuxContainer>;
public sealed class WorkspaceRunStandaloneFileSdk10Linux : WorkspaceRunStandaloneFileTests<Sdk10LinuxContainer>;