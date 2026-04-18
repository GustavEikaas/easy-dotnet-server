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

  /// <summary>
  /// Tracks the active RPC call so that <c>ReceiveXxx</c> helpers in derived classes can race
  /// against it. If the scope task completes before an expected reverse request arrives, those
  /// helpers throw immediately instead of waiting for their full timeout.
  /// Set by calling <see cref="BeginCall"/>.
  /// </summary>
  protected Task? _rpcScope;

  /// <summary>
  /// Starts an RPC call, wraps it with a hard timeout, stores it as the active scope, and
  /// returns the wrapped task. If the server does not respond within <paramref name="timeout"/>
  /// the task faults with <see cref="TimeoutException"/> and the test fails immediately rather
  /// than hanging indefinitely in CI.
  /// Derived classes expose typed wrappers (e.g. <c>BeginRun</c>) that call this.
  /// </summary>
  protected Task BeginCall(Task task, TimeSpan? timeout = null)
  {
    var timedTask = task.WaitAsync(timeout ?? TimeSpan.FromSeconds(30));
    _rpcScope = timedTask;
    return timedTask;
  }

  /// <summary>
  /// Initializes the server with the given workspace.
  /// When <see cref="TempWorkspace.SolutionPath"/> is non-null the server is pointed at that solution;
  /// otherwise heuristic project discovery is used (no solution file).
  /// </summary>
  protected Task<TestInitializeResponse> InitializeWorkspaceAsync(TempWorkspace ws) =>
    Container.Rpc.InvokeWithParameterObjectAsync<TestInitializeResponse>(
      "initialize",
      new List<TestInitializeRequest>
      {
        new(DefaultClientInfo, ws.SolutionPath is { } solutionPath
          ? new TestProjectInfo(Path.GetDirectoryName(solutionPath)!, solutionPath)
          : new TestProjectInfo(ws.RootDir))
      });
}