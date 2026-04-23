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
  private readonly ConcurrentDictionary<string, string> _lastStatusKind = new();

  /// <summary>All nodes registered so far, keyed by <see cref="TestNodeDto.Id"/>.</summary>
  public IReadOnlyDictionary<string, TestNodeDto> Nodes => _nodes;

  /// <summary>
  /// Last status discriminator seen for each node id via <c>updateStatus</c> /
  /// <c>updateStatusBatch</c> (e.g. <c>"Running"</c>, <c>"Passed"</c>, <c>"Failed"</c>).
  /// </summary>
  public IReadOnlyDictionary<string, string> LastStatusKind => _lastStatusKind;

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
    public void UpdateStatus(UpdateStatusPayload payload)
    {
      if (payload.Status is { } s) owner._lastStatusKind[payload.Id] = s.Type;
    }

    [JsonRpcMethod("updateStatusBatch", UseSingleObjectParameterDeserialization = true)]
    public void UpdateStatusBatch(UpdateStatusBatchPayload payload)
    {
      if (payload.Updates is null) return;
      foreach (var u in payload.Updates)
      {
        if (u.Id is { } id && u.Status is { } s) owner._lastStatusKind[id] = s.Type;
      }
    }

    [JsonRpcMethod("testrunner/statusUpdate", UseSingleObjectParameterDeserialization = true)]
    public void TestrunnerStatusUpdate(object _) { /* runner-level status, not asserted */ }

    [JsonRpcMethod("testrunner/isVisible")]
    public bool IsVisible() => true;
  }

  private sealed record RegisterTestPayload(TestNodeDto Test);
  private sealed record RemoveTestPayload(string Id);
  private sealed record StatusDto(string Type);
  private sealed record UpdateStatusPayload(string Id, StatusDto? Status, List<string>? AvailableActions);
  private sealed record UpdateStatusBatchPayload(List<StatusUpdate>? Updates);
  private sealed record StatusUpdate(string Id, StatusDto? Status, List<string>? AvailableActions);
}