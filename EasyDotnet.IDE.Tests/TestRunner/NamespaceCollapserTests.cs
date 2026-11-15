using EasyDotnet.IDE.TestRunner.Executor;
using EasyDotnet.IDE.TestRunner.Models;
using static EasyDotnet.IDE.Tests.TestRunner.NodeRegistryTests;

namespace EasyDotnet.IDE.Tests.TestRunner;

public class NamespaceCollapserTests
{
  [Test]
  public async Task Collapse_SingleChildChain_MergesIntoTerminal()
  {
    var nodes = Collapse(
        Node("A", "proj", new NodeType.Namespace()),
        Node("B", "A", new NodeType.Namespace()),
        Node("C", "B", new NodeType.Namespace()),
        Node("class", "C", new NodeType.TestClass()),
        Node("m1", "class", new NodeType.TestMethod()));

    await Assert.That(nodes.Select(n => n.Id).ToList()).IsEquivalentTo(["C", "class", "m1"]);

    var terminal = nodes.Single(n => n.Id == "C");
    await Assert.That(terminal.DisplayName).IsEqualTo("A.B.C");
    await Assert.That(terminal.ParentId).IsEqualTo("proj");
    await AssertSelfContained(nodes);
  }

  [Test]
  public async Task Collapse_BranchingNamespace_StopsAtBranch()
  {
    var nodes = Collapse(
        Node("A", "proj", new NodeType.Namespace()),
        Node("B", "A", new NodeType.Namespace()),
        Node("C", "B", new NodeType.Namespace()),
        Node("D", "B", new NodeType.Namespace()),
        Node("classC", "C", new NodeType.TestClass()),
        Node("classD", "D", new NodeType.TestClass()));

    await Assert.That(nodes.Select(n => n.Id).ToList()).IsEquivalentTo(["B", "C", "D", "classC", "classD"]);
    await Assert.That(nodes.Single(n => n.Id == "B").DisplayName).IsEqualTo("A.B");
    await Assert.That(nodes.Single(n => n.Id == "C").ParentId).IsEqualTo("B");
    await AssertSelfContained(nodes);
  }

  // The executor registers exactly the collapsed list, so every parent must be the project or
  // another emitted node — otherwise the registry and the client tree disagree, and
  // rediscovery would removeTest ids the client never had.
  private static async Task AssertSelfContained(List<TestNode> nodes)
  {
    var ids = nodes.Select(n => n.Id).ToHashSet();
    var dangling = nodes.Where(n => n.ParentId != "proj" && !ids.Contains(n.ParentId!)).ToList();
    await Assert.That(dangling.Count).IsEqualTo(0);
  }

  private static List<TestNode> Collapse(params TestNode[] discovered)
  {
    var nodes = discovered.ToList();
    NamespaceCollapser.Collapse("proj", nodes);
    return nodes;
  }
}
