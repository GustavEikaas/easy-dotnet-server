using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.TestRunner.Fixtures;

namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// TUnit discovery tests via the MTP adapter against a host-built, real TUnit executable
/// bind-mounted into the container.
///
/// Covers the same two bug classes as the MSTest batch but through the MTP code path:
///   - Block-scoped namespaces inside a single file are each represented by their own node.
///   - Parameterised tests (<c>[Arguments]</c>) surface as Subcases under a TheoryGroup.
/// </summary>
public abstract class TUnitMtpDiscoveryTests<TContainer> : TestRunnerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Discover_MultipleNamespacesInOneFile_EachAppearsAsItsOwnLeafNamespace()
  {
    // Project root namespace is "Sample.TUnitMultiNs", tests live in Sample.Block.N1 / N2.
    // Only the leading "Sample" segment overlaps with the root, so the "Block" level
    // survives and gets collapsed into its descendants.
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.TUnitMultiNs")
      .WithFramework(TestFrameworkKind.TUnitMtp)
      .WithFile("MultiNs.cs", TestFixtures.TUnitBlockNamespaces)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    Assert.Single(NodesOfType(NodeTypeNames.Project));

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
  public async Task Discover_TestClassAndMethod_ReportCorrectSourceLocations()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.TUnitLocation")
      .WithFramework(TestFrameworkKind.TUnitMtp)
      .WithFile("Loc.cs", TestFixtures.TUnitLocationMarker)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var locPath = Path.Combine(fixture.ProjectDir, "Loc.cs");
    var testClass = Assert.Single(NodesOfType(NodeTypeNames.TestClass), n => n.DisplayName == "C");

    Assert.Equal(locPath, testClass.FilePath);
    Assert.Equal(TestFixtures.TUnitLocationLines.ClassSignature, testClass.SignatureLine);
    Assert.Equal(TestFixtures.TUnitLocationLines.ClassBodyStart, testClass.BodyStartLine);
    Assert.Equal(TestFixtures.TUnitLocationLines.ClassEnd, testClass.EndLine);

    var method = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "M");
    Assert.Equal(locPath, method.FilePath);
    Assert.Equal(TestFixtures.TUnitLocationLines.MethodSignature, method.SignatureLine);
    Assert.Equal(TestFixtures.TUnitLocationLines.MethodBodyStart, method.BodyStartLine);
    Assert.Equal(TestFixtures.TUnitLocationLines.MethodEnd, method.EndLine);
  }

  [Fact]
  public async Task Discover_ArgumentsParameterizedTest_ProducesTheoryGroupWithSubcases()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.TUnitParams")
      .WithFramework(TestFrameworkKind.TUnitMtp)
      .WithFile("Rows.cs", TestFixtures.TUnitParameterized)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var testClass = Assert.Single(NodesOfType(NodeTypeNames.TestClass), n => n.DisplayName == "Rows");

    var theoryGroup = Children(testClass.Id)
      .SingleOrDefault(n => n.Type.Type == NodeTypeNames.TheoryGroup && n.DisplayName == "M")
      ?? throw new Xunit.Sdk.XunitException(
        $"Expected a TheoryGroup named 'M' as the only child of TestClass 'Rows'. Node dump:\n{DumpNodes()}");

    var subcases = Children(theoryGroup.Id)
      .Where(n => n.Type.Type == NodeTypeNames.Subcase)
      .ToList();

    Assert.Equal(2, subcases.Count);
  }

  [Fact]
  public async Task Discover_TestWithCustomDisplayName_SurfacesDisplayNameOnMethodNode()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.TUnitCustomName")
      .WithFramework(TestFrameworkKind.TUnitMtp)
      .WithFile("CustomName.cs", TestFixtures.TUnitCustomDisplayName)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var method = Assert.Single(NodesOfType(NodeTypeNames.TestMethod));
    Assert.Equal("custom test name", method.DisplayName);
  }

  private string DumpNodes() =>
    string.Join("\n", Nodes.Values
      .OrderBy(n => n.Id)
      .Select(n => $"  [{n.Type.Type}] {n.Id} (display='{n.DisplayName}', parent={n.ParentId})"));
}

public sealed class TUnitMtpDiscoverySdk10Linux : TUnitMtpDiscoveryTests<Sdk10LinuxContainer>;