using EasyDotnet.ContainerTests.Scaffold;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Docker;

/// <summary>
/// Common lifecycle and helpers shared by all container integration tests.
/// Sets <see cref="ServerContainer.RpcConfigurator"/> via <see cref="ConfigureRpc"/> before
/// starting the container so reverse-request handlers are registered before the first message.
/// </summary>
public abstract class ContainerTestBase<TContainer> : IAsyncLifetime
  where TContainer : ServerContainer, new()
{
  private static readonly TestClientInfo DefaultClientInfo = new("test", "3.0.0");

  protected TContainer Container { get; } = new();

  public Task InitializeAsync()
  {
    Container.RpcConfigurator = ConfigureRpc;
    return Container.StartAsync();
  }

  public async Task DisposeAsync() => await Container.DisposeAsync();

  /// <summary>Override to register reverse-request handlers on the RPC connection.</summary>
  protected virtual void ConfigureRpc(JsonRpc rpc) { }

  protected Task<TestInitializeResponse> InitializeWorkspaceAsync(TempContainerSolution solution) =>
    Container.Rpc.InvokeWithParameterObjectAsync<TestInitializeResponse>(
      "initialize",
      new List<TestInitializeRequest>
      {
        new(DefaultClientInfo, new TestProjectInfo(
          Path.GetDirectoryName(solution.SolutionPath)!,
          solution.SolutionPath))
      });

  /// <summary>
  /// Initializes the workspace without a solution file, triggering the server's
  /// heuristic project-discovery (csproj scan) and single-file execution paths.
  /// </summary>
  protected Task<TestInitializeResponse> InitializeWorkspaceAsync(TempContainerWorkspace workspace) =>
    Container.Rpc.InvokeWithParameterObjectAsync<TestInitializeResponse>(
      "initialize",
      new List<TestInitializeRequest>
      {
        new(DefaultClientInfo, new TestProjectInfo(workspace.RootDir))
      });
}