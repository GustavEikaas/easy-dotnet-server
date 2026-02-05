using EasyDotnet.TestRunner.Models;
using EasyDotnet.TestRunner.Services;
using StreamJsonRpc;
using DiscoveredTest = EasyDotnet.Domain.Models.Test.DiscoveredTest;

namespace EasyDotnet.TestRunner.Tests.Hierarchy;

public class HierarchyServiceTests
{
  private readonly TestHierarchyService _sut;
  private readonly TestSessionRegistry _registry;

  public HierarchyServiceTests()
  {
    _sut = new TestHierarchyService();

    var dummyRpc = new JsonRpc(new HeaderDelimitedMessageHandler(Stream.Null, Stream.Null));
    dummyRpc.StartListening();

    _registry = new TestSessionRegistry(dummyRpc);
  }

  /// <summary>
  /// Scenario: A single linear namespace chain.
  /// <para>
  /// Input: <c>EasyDotnet.TestRunner.Services.MyTest</c>
  /// </para>
  /// <b>Expected Tree:</b>
  /// <code>
  /// root
  /// └── EasyDotnet.TestRunner.Services  [Namespace] (Collapsed)
  ///     └── TestMethod1                 [Test]
  /// </code>
  /// </summary>
  [Test]
  public async Task Should_Collapse_Single_Chain_Namespaces()
  {
    var tests = new[]
    {
      CreateTest("EasyDotnet.TestRunner.Services.MyTest", "TestMethod1")
    };

    const string rootId = "project-guid";

    _sut.ProcessTestDiscovery(rootId, tests, _registry);

    var nodes = _registry.GetAllNodes().ToList();

    var namespaces = nodes.Where(n => n.Type is NodeType.Namespace).ToList();

    await Assert.That(namespaces).HasCount(1);
    await Assert.That(namespaces[0].DisplayName).IsEqualTo("EasyDotnet.TestRunner.Services");
    await Assert.That(namespaces[0].ParentId).IsEqualTo(rootId);

    var testMethod = nodes.Single(n => n.Type is NodeType.TestMethod);
    await Assert.That(testMethod.ParentId).IsEqualTo(namespaces[0].Id);
  }

  /// <summary>
  /// Scenario: Namespaces split early.
  /// <para>
  /// Input:
  /// <br/><c>EasyDotnet.Common.UtilTest</c>
  /// <br/><c>EasyDotnet.Features.LoginTest</c>
  /// </para>
  /// <b>Expected Tree:</b>
  /// <code>
  /// root
  /// └── EasyDotnet        [Namespace] (Common Parent)
  ///     ├── Common        [Namespace]
  ///     │   └── TestA     [Test]
  ///     └── Features      [Namespace]
  ///         └── TestB     [Test]
  /// </code>
  /// </summary>
  [Test]
  public async Task Should_Not_Collapse_Branching_Namespaces()
  {
    var tests = new[]
    {
      CreateTest("EasyDotnet.Common.UtilTest", "TestA"),
      CreateTest("EasyDotnet.Features.LoginTest", "TestB")
    };

    const string rootId = "project-guid";

    _sut.ProcessTestDiscovery(rootId, tests, _registry);

    var nodes = _registry.GetAllNodes();

    var rootNs = nodes.SingleOrDefault(n => n.DisplayName == "EasyDotnet");
    await Assert.That(rootNs).IsNotNull();
    await Assert.That(rootNs!.ParentId).IsEqualTo(rootId);

    var common = nodes.Single(n => n.DisplayName == "Common");
    var features = nodes.Single(n => n.DisplayName == "Features");

    await Assert.That(common.ParentId).IsEqualTo(rootNs.Id);
    await Assert.That(features.ParentId).IsEqualTo(rootNs.Id);
  }

  /// <summary>
  /// Scenario: Long common prefix, then a split.
  /// <para>
  /// Input:
  /// <br/><c>EasyDotnet.Tests.Unit.Test1</c>
  /// <br/><c>EasyDotnet.Tests.Integration.Test2</c>
  /// </para>
  /// <b>Expected Tree:</b>
  /// <code>
  /// root
  /// └── EasyDotnet.Tests    [Namespace] (Collapsed)
  ///     ├── Unit            [Namespace]
  ///     │   └── Run         [Test]
  ///     └── Integration     [Namespace]
  ///         └── Run         [Test]
  /// </code>
  /// </summary>
  [Test]
  public async Task Should_Collapse_Parent_But_Split_Children()
  {
    var tests = new[]
    {
      CreateTest("EasyDotnet.Tests.Unit.Test1", "Run"),
      CreateTest("EasyDotnet.Tests.Integration.Test2", "Run")
    };

    const string rootId = "root";
    _sut.ProcessTestDiscovery(rootId, tests, _registry);

    var nodes = _registry.GetAllNodes();

    var collapsedRoot = nodes.SingleOrDefault(n => n.DisplayName == "EasyDotnet.Tests");
    await Assert.That(collapsedRoot).IsNotNull();
    await Assert.That(collapsedRoot!.ParentId).IsEqualTo(rootId);

    var unit = nodes.Single(n => n.DisplayName == "Unit");
    var integration = nodes.Single(n => n.DisplayName == "Integration");

    await Assert.That(unit.ParentId).IsEqualTo(collapsedRoot.Id);
    await Assert.That(integration.ParentId).IsEqualTo(collapsedRoot.Id);
  }

  /// <summary>
  /// Scenario: Parameterized Tests (Data Driven Tests).
  /// <para>
  /// Input:
  /// <br/><c>My.Tests.Calc.Add(1,2)</c>
  /// <br/><c>My.Tests.Calc.Add(3,4)</c>
  /// </para>
  /// <b>Expected Tree:</b>
  /// <code>
  /// root
  /// └── My.Tests     [Namespace] (Collapsed)
  ///     └── Add      [TestGroup] (The Method)
  ///         ├── (1,2) [Subcase]
  ///         └── (3,4) [Subcase]
  /// </code>
  /// </summary>
  [Test]
  public async Task Should_Handle_Parameterized_Test_Groups()
  {
    var tests = new[]
    {
      CreateTest("My.Tests.Calc.Add", "Add(1,2)"),
      CreateTest("My.Tests.Calc.Add", "Add(3,4)")
    };

    _sut.ProcessTestDiscovery("root", tests, _registry);

    var nodes = _registry.GetAllNodes();

    var ns = nodes.Single(n => n.Type is NodeType.Namespace);
    await Assert.That(ns.DisplayName).IsEqualTo("My.Tests.Calc");

    var group = nodes.Single(n => n.Type is NodeType.TestGroup);
    await Assert.That(group.DisplayName).IsEqualTo("Add");
    await Assert.That(group.ParentId).IsEqualTo(ns.Id);

    var subcases = nodes.Where(n => n.Type is NodeType.Subcase).ToList();
    await Assert.That(subcases).HasCount(2);

    foreach (var sub in subcases)
    {
      await Assert.That(sub.ParentId).IsEqualTo(group.Id);
      await Assert.That(sub.DisplayName).Contains(")");
    }
  }

  /// <summary>
  /// Scenario: MSTest DataRows with Custom Display Names. (gh#727)
  /// <para>
  /// The bug was that these were treated as unrelated single tests because they lack '()'
  /// and have different display names. They should be grouped by their common FQN.
  /// </para>
  /// <b>Input:</b>
  /// <br/><c>FQN: Test1.ValidateInput, Display: "Valid input string"</c>
  /// <br/><c>FQN: Test1.ValidateInput, Display: "Empty string"</c>
  /// <br/><c>FQN: Test1.ValidateInput, Display: "Null string"</c>
  /// <para>
  /// <b>Expected Tree:</b>
  /// <code>
  /// root
  /// └── Test1              [Namespace/Class]
  ///     └── ValidateInput  [TestGroup] (Derived from FQN)
  ///         ├── Valid input string [Subcase]
  ///         ├── Empty string       [Subcase]
  ///         └── Null string        [Subcase]
  /// </code>
  /// </para>
  /// </summary>
  [Test]
  public async Task Should_Group_MSTest_DataRows_With_Custom_DisplayNames()
  {
    const string fqn = "EasyDotnet.Testy.Test1.ValidateInput";

    var row1 = new DiscoveredTest
    {
      Id = "id-1",
      FullyQualifiedName = fqn,
      NamespacePath = ["EasyDotnet", "Testy", "Test1"],
      DisplayName = "Valid input string",
      Arguments = null,
      FilePath = "Test1.cs",
      LineNumber = 10
    };

    var row2 = new DiscoveredTest
    {
      Id = "id-2",
      FullyQualifiedName = fqn,
      NamespacePath = ["EasyDotnet", "Testy", "Test1"],
      DisplayName = "Empty string",
      Arguments = null,
      FilePath = "Test1.cs",
      LineNumber = 10
    };

    var row3 = new DiscoveredTest
    {
      Id = "id-3",
      FullyQualifiedName = fqn,
      NamespacePath = ["EasyDotnet", "Testy", "Test1"],
      DisplayName = "Null string",
      Arguments = null,
      FilePath = "Test1.cs",
      LineNumber = 10
    };

    _sut.ProcessTestDiscovery("root", [row1, row2, row3], _registry);

    var nodes = _registry.GetAllNodes();


    var group = nodes.Single(n => n.Type is NodeType.TestGroup);
    await Assert.That(group.DisplayName).IsEqualTo("ValidateInput");

    var subcases = nodes.Where(n => n.Type is NodeType.Subcase).ToList();
    await Assert.That(subcases).HasCount(3);

    await Assert.That(subcases.Select(s => s.DisplayName)).Contains("Valid input string");
    await Assert.That(subcases.Select(s => s.DisplayName)).Contains("Empty string");
    await Assert.That(subcases.Select(s => s.DisplayName)).Contains("Null string");

    foreach (var sub in subcases)
    {
      await Assert.That(sub.ParentId).IsEqualTo(group.Id);
    }
  }

  private static DiscoveredTest CreateTest(string fqn, string displayName)
  {
    string? args = null;
    var start = displayName.IndexOf('(');
    var end = displayName.LastIndexOf(')');

    if (start >= 0 && end > start)
    {
      args = displayName[start..(end + 1)];
    }

    var parts = fqn.Split('.', StringSplitOptions.RemoveEmptyEntries);
    var namespacePath = parts.Length > 0 ? parts[..^1] : [];

    return new DiscoveredTest
    {
      Id = Guid.NewGuid().ToString(),
      FullyQualifiedName = fqn,
      NamespacePath = namespacePath,
      DisplayName = displayName,
      Arguments = args,
      FilePath = "c:/test.cs",
      LineNumber = 10
    };
  }
}