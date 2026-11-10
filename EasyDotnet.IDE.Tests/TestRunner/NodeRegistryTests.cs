using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;

namespace EasyDotnet.IDE.Tests.TestRunner;

public class NodeRegistryTests
{
  [Test]
  public async Task GetChildren_ReturnsOnlyDirectChildren()
  {
    var registry = BuildTree();

    var children = registry.GetChildren("proj").Select(n => n.Id).Order().ToList();

    await Assert.That(children).IsEquivalentTo(["ns"]);
  }

  [Test]
  public async Task GetDescendants_ReturnsWholeSubtree()
  {
    var registry = BuildTree();

    var descendants = registry.GetDescendants("proj").Select(n => n.Id).Order().ToList();

    await Assert.That(descendants).IsEquivalentTo(["class", "m1", "m2", "ns"]);
    await Assert.That(registry.HasDescendants("proj")).IsTrue();
    await Assert.That(registry.HasDescendants("m1")).IsFalse();
  }

  [Test]
  public async Task Register_SameIdWithNewParent_MovesNodeBetweenParents()
  {
    var registry = BuildTree();
    registry.Register(Node("class2", "ns", new NodeType.TestClass()));

    registry.Register(Node("m1", "class2", new NodeType.TestMethod()));

    await Assert.That(registry.GetChildren("class").Select(n => n.Id).ToList()).IsEquivalentTo(["m2"]);
    await Assert.That(registry.GetChildren("class2").Select(n => n.Id).ToList()).IsEquivalentTo(["m1"]);
    await Assert.That(registry.GetLeafCount()).IsEqualTo(2);
  }

  [Test]
  public async Task Register_SameIdSameParent_DoesNotDuplicateChild()
  {
    var registry = BuildTree();

    registry.Register(Node("m1", "class", new NodeType.TestMethod()) with { DisplayName = "renamed" });

    var children = registry.GetChildren("class").ToList();
    await Assert.That(children.Count).IsEqualTo(2);
    await Assert.That(children.Single(n => n.Id == "m1").DisplayName).IsEqualTo("renamed");
    await Assert.That(registry.GetLeafCount()).IsEqualTo(2);
  }

  [Test]
  public async Task Register_ChangingLeafType_UpdatesLeafCount()
  {
    var registry = BuildTree();

    registry.Register(Node("m1", "class", new NodeType.ProbableTest()));

    await Assert.That(registry.GetLeafCount()).IsEqualTo(1);
  }

  [Test]
  public async Task ClearDescendants_RemovesSubtreeButKeepsRoot()
  {
    var registry = BuildTree();
    registry.Register(Node("m1", "class", new NodeType.TestMethod()), nativeId: "native-1");

    registry.ClearDescendants("proj");

    await Assert.That(registry.Exists("proj")).IsTrue();
    await Assert.That(registry.HasDescendants("proj")).IsFalse();
    await Assert.That(registry.GetDescendants("proj").Any()).IsFalse();
    await Assert.That(registry.Exists("m1")).IsFalse();
    await Assert.That(registry.GetStableId("native-1")).IsNull();
    await Assert.That(registry.GetLeafCount()).IsEqualTo(0);
    await Assert.That(registry.GetChildren("sln").Select(n => n.Id).ToList()).IsEquivalentTo(["proj"]);
  }

  [Test]
  public async Task ClearDescendants_ThenReRegister_RebuildsIndex()
  {
    var registry = BuildTree();

    registry.ClearDescendants("proj");
    registry.Register(Node("ns", "proj", new NodeType.Namespace()));
    registry.Register(Node("m3", "ns", new NodeType.TestMethod()));

    await Assert.That(registry.GetDescendants("proj").Select(n => n.Id).Order().ToList()).IsEquivalentTo(["m3", "ns"]);
    await Assert.That(registry.GetLeafCount()).IsEqualTo(1);
  }

  [Test]
  public async Task RemoveSubtree_RemovesRootAndDescendants_RootLast()
  {
    var registry = BuildTree();

    var removed = registry.RemoveSubtree("class");

    await Assert.That(removed.Last()).IsEqualTo("class");
    await Assert.That(removed.Order().ToList()).IsEquivalentTo(["class", "m1", "m2"]);
    await Assert.That(registry.GetChildren("ns").Any()).IsFalse();
    await Assert.That(registry.HasDescendants("ns")).IsFalse();
    await Assert.That(registry.GetLeafCount()).IsEqualTo(0);
  }

  [Test]
  public async Task RemoveSubtree_UnknownRoot_ReturnsEmpty()
  {
    var registry = BuildTree();

    await Assert.That(registry.RemoveSubtree("missing").Count).IsEqualTo(0);
    await Assert.That(registry.GetLeafCount()).IsEqualTo(2);
  }

  [Test]
  public async Task UpdateLineNumbers_KeepsNodeUnderSameParent()
  {
    var registry = BuildTree();

    var changed = registry.UpdateLineNumbers("m1", 10, 11, 20);

    await Assert.That(changed).IsTrue();
    var m1 = registry.GetChildren("class").Single(n => n.Id == "m1");
    await Assert.That(m1.SignatureLine).IsEqualTo(10);
    await Assert.That(registry.GetChildren("class").Count()).IsEqualTo(2);
  }

  [Test]
  public async Task Clear_ResetsIndexAndLeafCount()
  {
    var registry = BuildTree();

    registry.Clear();

    await Assert.That(registry.GetChildren("sln").Any()).IsFalse();
    await Assert.That(registry.HasDescendants("sln")).IsFalse();
    await Assert.That(registry.GetLeafCount()).IsEqualTo(0);
  }

  [Test]
  public async Task GetLeafCount_CountsOnlyTestMethodsAndSubcases()
  {
    var registry = BuildTree();
    registry.Register(Node("theory", "class", new NodeType.TheoryGroup()));
    registry.Register(Node("case1", "theory", new NodeType.Subcase()));
    registry.Register(Node("probable", "class", new NodeType.ProbableTest()));

    await Assert.That(registry.GetLeafCount()).IsEqualTo(3);
    await Assert.That(registry.GetLeafDescendants("class").Count()).IsEqualTo(4);
  }

  [Test]
  public async Task ConcurrentRegisterAndClear_KeepsIndexConsistent()
  {
    var registry = new NodeRegistry();
    registry.Register(Node("sln", null, new NodeType.Solution()));

    // Mirrors multi-project discovery: each project re-discovered in parallel.
    var projects = Enumerable.Range(0, 8).Select(p => $"proj{p}").ToList();
    foreach (var p in projects)
      registry.Register(Node(p, "sln", new NodeType.Project()));

    await Task.WhenAll(projects.Select(p => Task.Run(() =>
    {
      for (var round = 0; round < 20; round++)
      {
        registry.ClearDescendants(p);
        registry.Register(Node($"{p}.class", p, new NodeType.TestClass()));
        for (var i = 0; i < 50; i++)
          registry.Register(Node($"{p}.m{i}", $"{p}.class", new NodeType.TestMethod()));
      }
    })));

    foreach (var p in projects)
      await Assert.That(registry.GetLeafDescendants(p).Count()).IsEqualTo(50);

    var leafCount = registry.GetAll().Count(n => n.Type is NodeType.TestMethod or NodeType.Subcase);
    await Assert.That(registry.GetLeafCount()).IsEqualTo(leafCount);
    await Assert.That(leafCount).IsEqualTo(8 * 50);
  }

  private static NodeRegistry BuildTree()
  {
    var registry = new NodeRegistry();
    registry.Register(Node("sln", null, new NodeType.Solution()));
    registry.Register(Node("proj", "sln", new NodeType.Project()));
    registry.Register(Node("ns", "proj", new NodeType.Namespace()));
    registry.Register(Node("class", "ns", new NodeType.TestClass()));
    registry.Register(Node("m1", "class", new NodeType.TestMethod()));
    registry.Register(Node("m2", "class", new NodeType.TestMethod()));
    return registry;
  }

  internal static TestNode Node(string id, string? parentId, NodeType type) =>
      new(
          Id: id,
          DisplayName: id,
          ParentId: parentId,
          FilePath: null,
          SignatureLine: null,
          BodyStartLine: null,
          EndLine: null,
          Type: type,
          ProjectId: null,
          AvailableActions: []);
}
