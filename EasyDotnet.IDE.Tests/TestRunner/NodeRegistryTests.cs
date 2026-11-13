using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;

namespace EasyDotnet.IDE.Tests.TestRunner;

public class NodeRegistryTests
{
  [Test]
  public async Task Register_WithNativeTest_KeepsRunHandleUntilNodeIsReplacedOrRemoved()
  {
    var registry = BuildTree();
    var firstHandle = new object();
    var secondHandle = new object();

    registry.Register(Node("m1", "class", new NodeType.TestMethod()), new NativeTest("native-1", firstHandle));
    await Assert.That(registry.GetNativeTest("m1")?.RunHandle).IsSameReferenceAs(firstHandle);

    registry.Register(Node("m1", "class", new NodeType.TestMethod()), new NativeTest("native-1", secondHandle));
    await Assert.That(registry.GetNativeTest("m1")?.RunHandle).IsSameReferenceAs(secondHandle);

    registry.RemoveSubtree("class");
    await Assert.That(registry.GetNativeTest("m1")).IsNull();
    await Assert.That(registry.GetStableId("native-1")).IsNull();
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