using EasyDotnet.Domain.Models.MTP;
using EasyDotnet.Infrastructure.Services;
using EasyDotnet.TestRunner.Models;
using EasyDotnet.TestRunner.Services;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using StreamJsonRpc;

namespace EasyDotnet.TestRunner.Tests.Executors;

public class EngineNormalizationTests
{
  private readonly TestHierarchyService _sut;
  private readonly TestSessionRegistry _registry;
  private const string RootId = "root-project";

  public EngineNormalizationTests()
  {
    _sut = new TestHierarchyService();
    var dummyRpc = new JsonRpc(new HeaderDelimitedMessageHandler(Stream.Null, Stream.Null));
    dummyRpc.StartListening();
    _registry = new TestSessionRegistry(dummyRpc);
  }

  [Test]
  public async Task MTP_TUnit_Should_Combine_Type_And_Method()
  {
    var mtpNode = new TestNodeUpdate(
        Node: new(
          Uid: "tunit-id",
          DisplayName: "Parse_ValidRequest",
          TestNamespace: null,
          TestMethod: "Parse_ValidRequest",
          TestType: "EasyDotnet.Debugger.Tests.DapMessageDeserializerTests",
          FilePath: "/src/test.cs",
          LineStart: 10, // 1-based
          LineEnd: 15,
          Message: null, StackTrace: null, Duration: null, NodeType: "test", ExecutionState: "discovered", StandardOutput: null
          ),
        ParentUid: "");


    var discovered = mtpNode.ToDiscoveredTest();
    _sut.ProcessTestDiscovery(RootId, [discovered], _registry);

    var nodes = _registry.GetAllNodes();

    await Assert.That(discovered.FullyQualifiedName).IsEqualTo("EasyDotnet.Debugger.Tests.DapMessageDeserializerTests.Parse_ValidRequest");

    await Assert.That(discovered.LineNumber).IsEqualTo(9);

    var leaf = nodes.Single(n => n.Type is NodeType.TestMethod);
    await Assert.That(leaf.DisplayName).IsEqualTo("Parse_ValidRequest");
  }

  [Test]
  public async Task MTP_Expecto_Should_Handle_Missing_Type()
  {
    var mtpNode = new TestNodeUpdate(
        Node: new(
            Uid: "expecto-id",
            DisplayName: "samples.universe exists",
            TestNamespace: null,
            TestMethod: null!,
            TestType: null!,
            FilePath: "test.fs",
            LineStart: 1,
            LineEnd: 1,
            Message: null, StackTrace: null, Duration: null, NodeType: "test", ExecutionState: "discovered", StandardOutput: null
        ),
        ParentUid: ""
    );

    var discovered = mtpNode.ToDiscoveredTest();
    _sut.ProcessTestDiscovery(RootId, [discovered], _registry);

    await Assert.That(discovered.FullyQualifiedName).IsEqualTo("samples.universe exists");

    var nodes = _registry.GetAllNodes();
    var parent = nodes.First(n => n.DisplayName == "samples");
    var leaf = nodes.First(n => n.DisplayName == "universe exists");

    await Assert.That(leaf.ParentId).IsEqualTo(parent.Id);
  }

  [Test]
  public async Task VSTest_xUnit_Should_Clean_Noisy_DisplayNames()
  {
    var tc = new TestCase
    {
      Id = Guid.NewGuid(),
      FullyQualifiedName = "MyNamespace.MyClass.MyTest",
      DisplayName = "MyNamespace.MyClass.MyTest",
      LineNumber = 10,
      CodeFilePath = "test.cs",
      ExecutorUri = new Uri("executor://xunit")
    };

    var discovered = tc.ToDiscoveredTest();
    _sut.ProcessTestDiscovery(RootId, [discovered], _registry);

    var nodes = _registry.GetAllNodes();
    var leaf = nodes.Single(n => n.Type is NodeType.TestMethod);

    await Assert.That(leaf.DisplayName).IsEqualTo("MyTest");

    await Assert.That(discovered.FullyQualifiedName).IsEqualTo("MyNamespace.MyClass.MyTest");
  }

  [Test]
  public async Task VSTest_MSTest_Should_Keep_Clean_DisplayNames()
  {
    var tc = new TestCase
    {
      Id = Guid.NewGuid(),
      FullyQualifiedName = "MyNamespace.MyClass.MyTest",
      DisplayName = "MyTest",
      LineNumber = 10,
      CodeFilePath = "test.cs",
      ExecutorUri = new Uri("executor://mstest")
    };

    var discovered = tc.ToDiscoveredTest();

    await Assert.That(discovered.DisplayName).IsEqualTo("MyTest");
  }
}