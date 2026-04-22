using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.TestRunner.Fixtures;

namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// MSTest (VSTest form) discovery tests against a host-built, real MSTest assembly
/// bind-mounted into the container.
///
/// Batch 1 covers the two bug classes worth protecting long-term from issue #841:
///   - Block-scoped namespaces inside a single file are each represented by their own node.
///   - <c>[DataRow(DisplayName = ...)]</c> surfaces that display name on subcase nodes.
/// </summary>
public abstract class MsTestVsTestDiscoveryTests<TContainer> : TestRunnerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Discover_MultipleNamespacesInOneFile_EachAppearsAsItsOwnLeafNamespace()
  {
    // Project root namespace is "Mst.MultiNs", so namespaces starting with "Mst.MultiNs"
    // would be stripped by the executor. We use Mst.Block.N1 / Mst.Block.N2 so the
    // namespace chain survives intact and the common "Mst.Block" prefix gets collapsed.
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Mst.MultiNs")
      .WithFramework(TestFrameworkKind.MsTestVsTest)
      .WithFile("MultiNs.cs", TestFixtures.MsTestBlockNamespaces)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var project = Assert.Single(NodesOfType(NodeTypeNames.Project));

    var leafNamespaces = NodesOfType(NodeTypeNames.Namespace)
      .Where(n => Children(n.Id).Any(c => c.Type.Type == NodeTypeNames.TestClass))
      .OrderBy(n => n.DisplayName, StringComparer.Ordinal)
      .ToList();

    Assert.Equal(2, leafNamespaces.Count);
    Assert.Equal("N1", leafNamespaces[0].DisplayName);
    Assert.Equal("N2", leafNamespaces[1].DisplayName);

    foreach (var ns in leafNamespaces)
    {
      var classes = Children(ns.Id).Where(c => c.Type.Type == NodeTypeNames.TestClass).ToList();
      Assert.Single(classes);
      var methods = Children(classes[0].Id).Where(c => c.Type.Type == NodeTypeNames.TestMethod).ToList();
      Assert.Single(methods);
      Assert.Equal("M", methods[0].DisplayName);
    }
  }

  [Fact]
  public async Task Discover_DataRowWithDisplayName_ProducesTheoryGroupWithSubcases()
  {
    // EXPECTED TO FAIL on main as of 2026-04-22 — this locks in the fix for the bug reported at the bottom of issue #841.
    // Root cause: VsTestExtensions.ParseArguments only detects parameterised rows by
    // looking for a "(args)" suffix in TestCase.DisplayName. When MSTest's
    // [DataRow(DisplayName = "...")] sets a custom display name without parens, the
    // adapter thinks each row is a standalone method and emits flat TestMethod siblings
    // ("Rows::first case", "Rows::second case") instead of grouping them as Subcases
    // under a TheoryGroup for method M.
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Mst.DataRows")
      .WithFramework(TestFrameworkKind.MsTestVsTest)
      .WithFile("DataRows.cs", TestFixtures.MsTestDataRowDisplayName)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var testClass = Assert.Single(NodesOfType(NodeTypeNames.TestClass), n => n.DisplayName == "Rows");

    var theoryGroup = Children(testClass.Id)
      .SingleOrDefault(n => n.Type.Type == NodeTypeNames.TheoryGroup && n.DisplayName == "M")
      ?? throw new Xunit.Sdk.XunitException(
        $"Expected a TheoryGroup named 'M' as the only child of TestClass 'Rows'. Node dump:\n{DumpNodes()}");

    var subcases = Children(theoryGroup.Id)
      .Where(n => n.Type.Type == NodeTypeNames.Subcase)
      .Select(n => n.DisplayName)
      .OrderBy(n => n, StringComparer.Ordinal)
      .ToList();

    Assert.Equal(new[] { "first case", "second case" }, subcases);
  }

  [Fact]
  public async Task Discover_DynamicDataSource_ProducesTheoryGroupWithSubcases()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Mst.DynamicData")
      .WithFramework(TestFrameworkKind.MsTestVsTest)
      .WithFile("Rows.cs", TestFixtures.MsTestDynamicData)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var testClass = Assert.Single(NodesOfType(NodeTypeNames.TestClass), n => n.DisplayName == "Rows");
    var theoryGroup = Children(testClass.Id)
      .SingleOrDefault(n => n.Type.Type == NodeTypeNames.TheoryGroup && n.DisplayName == "M")
      ?? throw new Xunit.Sdk.XunitException(
        $"Expected a TheoryGroup named 'M'. Node dump:\n{DumpNodes()}");

    var subcases = Children(theoryGroup.Id).Where(n => n.Type.Type == NodeTypeNames.Subcase).ToList();
    Assert.Equal(2, subcases.Count);
  }

  [Fact]
  public async Task Discover_TestMethodWithCustomDisplayName_SurfacesDisplayNameOnMethodNode()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Mst.CustomName")
      .WithFramework(TestFrameworkKind.MsTestVsTest)
      .WithFile("CustomName.cs", TestFixtures.MsTestCustomDisplayName)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var method = Assert.Single(NodesOfType(NodeTypeNames.TestMethod));
    Assert.Equal("custom method name", method.DisplayName);
  }

  private string DumpNodes() =>
    string.Join("\n", Nodes.Values
      .OrderBy(n => n.Id)
      .Select(n => $"  [{n.Type.Type}] {n.Id} (display='{n.DisplayName}', parent={n.ParentId})"));
}

public sealed class MsTestVsTestDiscoverySdk10Linux : MsTestVsTestDiscoveryTests<Sdk10LinuxContainer>;