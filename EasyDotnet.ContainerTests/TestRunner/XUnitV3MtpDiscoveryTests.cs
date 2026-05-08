using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.TestRunner.Fixtures;

namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// xUnit v3 (MTP adapter) discovery tests against a host-built, real xUnit v3
/// executable bind-mounted into the container.
/// </summary>
public abstract class XUnitV3MtpDiscoveryTests<TContainer> : TestRunnerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Discover_MultipleNamespacesInOneFile_EachAppearsAsItsOwnLeafNamespace()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.XUnitV3MultiNs")
      .WithFramework(TestFrameworkKind.XUnitV3Mtp)
      .WithFile("MultiNs.cs", TestFixtures.XUnitBlockNamespaces)
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

  [Fact(Skip = "xUnit v3 1.0.1 does not emit FilePath / LineStart on MTP TestNodeUpdate, " +
               "so TestClass / TestMethod nodes come through with FilePath=null. Revisit when " +
               "xunit.v3 starts populating source location metadata in its MTP output.")]
  public async Task Discover_TestClassAndMethod_ReportCorrectSourceLocations()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.XUnitV3Location")
      .WithFramework(TestFrameworkKind.XUnitV3Mtp)
      .WithFile("Loc.cs", TestFixtures.XUnitLocationMarker)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var locPath = Path.Combine(fixture.ProjectDir, "Loc.cs");
    var testClass = Assert.Single(NodesOfType(NodeTypeNames.TestClass), n => n.DisplayName == "C");

    Assert.Equal(locPath, testClass.FilePath);
    Assert.Equal(TestFixtures.XUnitLocationLines.ClassSignature, testClass.SignatureLine);
    Assert.Equal(TestFixtures.XUnitLocationLines.ClassBodyStart, testClass.BodyStartLine);
    Assert.Equal(TestFixtures.XUnitLocationLines.ClassEnd, testClass.EndLine);

    var method = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "M");
    Assert.Equal(locPath, method.FilePath);
    Assert.Equal(TestFixtures.XUnitLocationLines.MethodSignature, method.SignatureLine);
    Assert.Equal(TestFixtures.XUnitLocationLines.MethodBodyStart, method.BodyStartLine);
    Assert.Equal(TestFixtures.XUnitLocationLines.MethodEnd, method.EndLine);
  }

  [Fact]
  public async Task Discover_InlineDataTheory_ProducesTheoryGroupWithSubcases()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.XUnitV3Params")
      .WithFramework(TestFrameworkKind.XUnitV3Mtp)
      .WithFile("Rows.cs", TestFixtures.XUnitInlineData)
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
  public async Task Discover_InlineDataWithComplexArgs_EachRowSurvivesAsSubcase()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.XUnitV3ComplexArgs")
      .WithFramework(TestFrameworkKind.XUnitV3Mtp)
      .WithFile("Rows.cs", TestFixtures.XUnitComplexArgs)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var testClass = Assert.Single(NodesOfType(NodeTypeNames.TestClass), n => n.DisplayName == "Rows");
    var theoryGroup = Children(testClass.Id)
      .SingleOrDefault(n => n.Type.Type == NodeTypeNames.TheoryGroup && n.DisplayName == "M")
      ?? throw new Xunit.Sdk.XunitException(
        $"Expected a TheoryGroup named 'M'. Node dump:\n{DumpNodes()}");

    var subcases = Children(theoryGroup.Id).Where(n => n.Type.Type == NodeTypeNames.Subcase).ToList();
    Assert.Equal(3, subcases.Count);
  }

  [Fact]
  public async Task Discover_MemberDataTheory_ProducesTheoryGroupWithSubcases()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.XUnitV3MemberData")
      .WithFramework(TestFrameworkKind.XUnitV3Mtp)
      .WithFile("Rows.cs", TestFixtures.XUnitMemberData)
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
  public async Task Discover_FactWithCustomDisplayName_SurfacesDisplayNameOnMethodNode()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.XUnitV3CustomName")
      .WithFramework(TestFrameworkKind.XUnitV3Mtp)
      .WithFile("CustomName.cs", TestFixtures.XUnitCustomDisplayName)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var method = Assert.Single(NodesOfType(NodeTypeNames.TestMethod));
    Assert.Equal("custom fact name", method.DisplayName);
  }

  private string DumpNodes() =>
    string.Join("\n", Nodes.Values
      .OrderBy(n => n.Id)
      .Select(n => $"  [{n.Type.Type}] {n.Id} (display='{n.DisplayName}', parent={n.ParentId})"));
}

public sealed class XUnitV3MtpDiscoverySdk10Linux : XUnitV3MtpDiscoveryTests<Sdk10LinuxContainer>;