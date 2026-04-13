using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests;

/// <summary>
/// Verifies that workspace/run:
///   1. Sends a promptSelection reverse request when no default is set
///   2. Persists the picked project to the settings file on disk
///   3. On a second call with UseDefault=true, skips the picker entirely
/// </summary>
public abstract class WorkspaceRunContainerTests<TContainer> : IAsyncLifetime
  where TContainer : ServerContainer, new()
{
  private static readonly TestClientInfo ClientInfo = new("test", "3.0.0");

  private int _selectionCallCount;
  private string? _pickedProjectPath;

  protected TContainer Container { get; } = new TContainer();

  public Task InitializeAsync()
  {
    Container.RpcConfigurator = rpc =>
      rpc.AddLocalRpcTarget(new TestClientHandlers(this),
        new JsonRpcTargetOptions { DisposeOnDisconnect = false });

    return Container.StartAsync();
  }

  public async Task DisposeAsync() => await Container.DisposeAsync();

  [Fact]
  public async Task Run_WithTwoRunnableProjects_PersistsDefaultAndBypassesPickerOnSecondCall()
  {
    using var solution = new TempContainerSolution();

    await Container.Rpc.InvokeWithParameterObjectAsync<TestInitializeResponse>(
      "initialize",
      new List<TestInitializeRequest>
      {
        new(ClientInfo, new TestProjectInfo(
          Path.GetDirectoryName(solution.SolutionPath)!,
          solution.SolutionPath))
      });

    await Container.Rpc.InvokeWithParameterObjectAsync(
      "workspace/run",
      new { useDefault = false, useLaunchProfile = false, filePath = (string?)null, cliArgs = (string?)null });

    Assert.Equal(1, _selectionCallCount);
    Assert.NotNull(_pickedProjectPath);

    await Container.Rpc.InvokeWithParameterObjectAsync(
      "workspace/run",
      new { useDefault = true, useLaunchProfile = false, filePath = (string?)null, cliArgs = (string?)null });

    Assert.Equal(1, _selectionCallCount);
  }

  private sealed class TestClientHandlers(WorkspaceRunContainerTests<TContainer> test)
  {
    [JsonRpcMethod("promptSelection", UseSingleObjectParameterDeserialization = true)]
    public Task<string?> PromptSelection(TestPromptSelectionRequest request)
    {
      Interlocked.Increment(ref test._selectionCallCount);
      var picked = request.Choices[0];
      test._pickedProjectPath = picked.Id[..picked.Id.LastIndexOf(':')];
      return Task.FromResult<string?>(picked.Id);
    }

    [JsonRpcMethod("runCommandManaged", UseSingleObjectParameterDeserialization = true)]
    public Task<object> RunCommandManaged(object? _ = null) =>
      Task.FromResult<object>(new { processId = 0 });
  }
}

public sealed class WorkspaceRunSdk8Linux : WorkspaceRunContainerTests<Sdk8LinuxContainer>;