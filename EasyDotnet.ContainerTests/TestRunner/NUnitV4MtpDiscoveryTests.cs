using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.TestRunner.Fixtures;

namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// NUnit v4 (MTP adapter) discovery tests against a host-built, real NUnit v4 executable.
/// </summary>
public abstract class NUnitV4MtpDiscoveryTests<TContainer> : TestRunnerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Discover_MultipleNamespacesInOneFile_EachAppearsAsItsOwnLeafNamespace()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.NUnitV4MultiNs")
      .WithFramework(TestFrameworkKind.NUnitV4Mtp)
      .WithFile("MultiNs.cs", TestFixtures.NUnitBlockNamespaces)
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
  public async Task Discover_TestCaseParameterized_ProducesTheoryGroupWithSubcases()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.NUnitV4Params")
      .WithFramework(TestFrameworkKind.NUnitV4Mtp)
      .WithFile("Rows.cs", TestFixtures.NUnitTestCase)
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
  public async Task Discover_TestCaseWithComplexArgs_EachRowSurvivesAsSubcase()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.NUnitV4ComplexArgs")
      .WithFramework(TestFrameworkKind.NUnitV4Mtp)
      .WithFile("Rows.cs", TestFixtures.NUnitComplexArgs)
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
  public async Task Discover_TestCaseSource_ProducesTheoryGroupWithSubcases()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.NUnitV4TestCaseSource")
      .WithFramework(TestFrameworkKind.NUnitV4Mtp)
      .WithFile("Rows.cs", TestFixtures.NUnitTestCaseSource)
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

  [Fact(Skip = "NUnit has no standard attribute to override the display name of a plain [Test]. " +
               "Per-case names are only configurable via [TestCase(TestName=...)] or TestCaseSource, " +
               "both of which are covered by the parameterised-tests bucket.")]
  public void Discover_TestWithCustomDisplayName_SurfacesDisplayNameOnMethodNode() { }

  private string DumpNodes() =>
    string.Join("\n", Nodes.Values
      .OrderBy(n => n.Id)
      .Select(n => $"  [{n.Type.Type}] {n.Id} (display='{n.DisplayName}', parent={n.ParentId})"));
}

public sealed class NUnitV4MtpDiscoverySdk10Linux : NUnitV4MtpDiscoveryTests<Sdk10LinuxContainer>;
