using EasyDotnet.IDE.TestRunner.Executor;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;
using static EasyDotnet.IDE.Tests.TestRunner.NodeRegistryTests;

namespace EasyDotnet.IDE.Tests.TestRunner;

public class NamespaceCollapserTests
{
  [Test]
  public async Task Collapse_SingleChildChain_MergesIntoTerminal()
  {
    var registry = NewRegistry();
    var emitted = Discover(registry,
        Node("A", "proj", new NodeType.Namespace()),
        Node("B", "A", new NodeType.Namespace()),
        Node("C", "B", new NodeType.Namespace()),
        Node("class", "C", new NodeType.TestClass()),
        Node("m1", "class", new NodeType.TestMethod()));

    await Assert.That(emitted.Select(n => n.Id).ToList()).IsEquivalentTo(["C", "class", "m1"]);

    var terminal = emitted.Single(n => n.Id == "C");
    await Assert.That(terminal.DisplayName).IsEqualTo("A.B.C");
    await Assert.That(terminal.ParentId).IsEqualTo("proj");
  }

  [Test]
  public async Task Collapse_RegistryMatchesEmittedTree()
  {
    var registry = NewRegistry();
    var emitted = Discover(registry,
        Node("A", "proj", new NodeType.Namespace()),
        Node("B", "A", new NodeType.Namespace()),
        Node("C", "B", new NodeType.Namespace()),
        Node("class", "C", new NodeType.TestClass()),
        Node("m1", "class", new NodeType.TestMethod()));

    await Assert.That(registry.Exists("A")).IsFalse();
    await Assert.That(registry.Exists("B")).IsFalse();
    await Assert.That(registry.Get("C")!.ParentId).IsEqualTo("proj");
    await Assert.That(registry.Get("C")!.DisplayName).IsEqualTo("A.B.C");
    await Assert.That(registry.GetDescendants("proj").ToList()).IsEquivalentTo(emitted);
  }

  [Test]
  public async Task Collapse_BranchingNamespace_StopsAtBranch()
  {
    var registry = NewRegistry();
    var emitted = Discover(registry,
        Node("A", "proj", new NodeType.Namespace()),
        Node("B", "A", new NodeType.Namespace()),
        Node("C", "B", new NodeType.Namespace()),
        Node("D", "B", new NodeType.Namespace()),
        Node("classC", "C", new NodeType.TestClass()),
        Node("classD", "D", new NodeType.TestClass()));

    await Assert.That(emitted.Select(n => n.Id).ToList()).IsEquivalentTo(["B", "C", "D", "classC", "classD"]);
    await Assert.That(emitted.Single(n => n.Id == "B").DisplayName).IsEqualTo("A.B");
    await Assert.That(emitted.Single(n => n.Id == "C").ParentId).IsEqualTo("B");
    await Assert.That(registry.GetDescendants("proj").ToList()).IsEquivalentTo(emitted);
  }

  [Test]
  public async Task Rediscovery_ProducesNoOrphansForCollapsedNamespaces()
  {
    var registry = NewRegistry();
    TestNode[] discovered =
    [
      Node("A", "proj", new NodeType.Namespace()),
      Node("B", "A", new NodeType.Namespace()),
      Node("class", "B", new NodeType.TestClass()),
      Node("m1", "class", new NodeType.TestMethod()),
    ];
    Discover(registry, discovered);

    // Same bookkeeping as OperationExecutor.DiscoverProjectAsync: anything present before
    // rediscovery but not emitted after it is sent to the client as removeTest.
    var preDiscoveryIds = registry.GetDescendants("proj").Select(n => n.Id).ToHashSet();
    var emitted = Discover(registry, discovered);
    var orphans = preDiscoveryIds.Except(emitted.Select(n => n.Id)).ToList();

    await Assert.That(orphans.Count).IsEqualTo(0);
  }

  private static NodeRegistry NewRegistry()
  {
    var registry = new NodeRegistry();
    registry.Register(Node("sln", null, new NodeType.Solution()));
    registry.Register(Node("proj", "sln", new NodeType.Project()));
    return registry;
  }

  // Mirrors DiscoverProjectAsync: clear, register every discovered node, then collapse.
  private static List<TestNode> Discover(NodeRegistry registry, params TestNode[] discovered)
  {
    registry.ClearDescendants("proj");
    foreach (var node in discovered)
      registry.Register(node);

    var pendingEmit = discovered.ToList();
    NamespaceCollapser.Collapse("proj", pendingEmit, registry);
    return pendingEmit;
  }
}
