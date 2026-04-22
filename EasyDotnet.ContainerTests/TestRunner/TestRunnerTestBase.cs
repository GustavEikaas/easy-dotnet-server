using System.Collections.Concurrent;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.TestRunner.Fixtures;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// Base for testrunner container tests. Registers RPC handlers for the reverse
/// notifications the server fires during discovery (<c>registerTest</c>,
/// <c>removeTest</c>, <c>updateStatus</c>, <c>updateStatusBatch</c>,
/// <c>testrunner/statusUpdate</c>) and the reverse request <c>testrunner/isVisible</c>,
/// then accumulates every registered node into <see cref="Nodes"/>.
/// <para>
/// Typical flow:
/// <code>
///   using var fixture = new TestProjectFixtureBuilder().…Build();
///   await InitializeTestRunnerAsync(fixture);
///   var method = FindByDisplayName("MyTest");
///   Assert.…
/// </code>
/// </para>
/// </summary>
public abstract class TestRunnerTestBase<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private static readonly TimeSpan InitializeTimeout = TimeSpan.FromMinutes(2);

  private readonly ConcurrentDictionary<string, TestNodeDto> _nodes = new();

  /// <summary>All nodes registered so far, keyed by <see cref="TestNodeDto.Id"/>.</summary>
  public IReadOnlyDictionary<string, TestNodeDto> Nodes => _nodes;

  protected override void ConfigureRpc(JsonRpc rpc) =>
    rpc.AddLocalRpcTarget(new RpcHandlers(this), new JsonRpcTargetOptions { DisposeOnDisconnect = false });

  /// <summary>
  /// Initializes the workspace against <paramref name="fixture"/>'s solution (or project root
  /// if no solution was written), then invokes <c>testrunner/initialize</c> and awaits completion.
  /// After this returns, <see cref="Nodes"/> contains the full discovered tree.
  /// </summary>
  protected async Task InitializeTestRunnerAsync(TestProjectFixture fixture)
  {
    var solutionPath = fixture.SolutionPath
      ?? throw new InvalidOperationException(
        "Fixture was built without a solution; testrunner/initialize needs a solution path.");

    await Container.Rpc.InvokeWithParameterObjectAsync<object>(
      "initialize",
      new List<object> { new { clientInfo = new { name = "test", version = "3.0.0" }, projectInfo = new { rootDir = fixture.RootDir, solutionFile = solutionPath } } });

    await BeginCall(Container.Rpc.TestRunnerInitializeAsync(solutionPath), InitializeTimeout);
  }

  /// <summary>Returns every node with <see cref="TestNodeDto.Type"/>.<c>Type</c> equal to <paramref name="typeName"/>.</summary>
  protected IEnumerable<TestNodeDto> NodesOfType(string typeName) =>
    _nodes.Values.Where(n => n.Type.Type == typeName);

  /// <summary>Returns every direct child of <paramref name="parentId"/>.</summary>
  protected IEnumerable<TestNodeDto> Children(string parentId) =>
    _nodes.Values.Where(n => n.ParentId == parentId);

  /// <summary>Returns nodes whose <see cref="TestNodeDto.DisplayName"/> matches exactly.</summary>
  protected IEnumerable<TestNodeDto> FindByDisplayName(string displayName) =>
    _nodes.Values.Where(n => n.DisplayName == displayName);

  private sealed class RpcHandlers(TestRunnerTestBase<TContainer> owner)
  {
    [JsonRpcMethod("registerTest", UseSingleObjectParameterDeserialization = true)]
    public void RegisterTest(RegisterTestPayload payload) =>
      owner._nodes[payload.Test.Id] = payload.Test;

    [JsonRpcMethod("removeTest", UseSingleObjectParameterDeserialization = true)]
    public void RemoveTest(RemoveTestPayload payload) =>
      owner._nodes.TryRemove(payload.Id, out _);

    [JsonRpcMethod("updateStatus", UseSingleObjectParameterDeserialization = true)]
    public void UpdateStatus(UpdateStatusPayload _) { /* not asserted in batch 1 */ }

    [JsonRpcMethod("updateStatusBatch", UseSingleObjectParameterDeserialization = true)]
    public void UpdateStatusBatch(UpdateStatusBatchPayload _) { /* not asserted in batch 1 */ }

    [JsonRpcMethod("testrunner/statusUpdate", UseSingleObjectParameterDeserialization = true)]
    public void TestrunnerStatusUpdate(object _) { /* runner-level status, not asserted */ }

    [JsonRpcMethod("testrunner/isVisible")]
    public bool IsVisible() => true;
  }

  private sealed record RegisterTestPayload(TestNodeDto Test);
  private sealed record RemoveTestPayload(string Id);
  private sealed record UpdateStatusPayload(string Id, object? Status, List<string>? AvailableActions);
  private sealed record UpdateStatusBatchPayload(List<object> Updates);
}
