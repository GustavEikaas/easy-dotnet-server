using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.TestRunner.Fixtures;

namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// xUnit v2 (VSTest adapter) discovery tests against a host-built, real xUnit assembly
/// bind-mounted into the container. Versions match <c>EasyDotnet.IntegrationTests</c>
/// (xunit 2.9.2 + xunit.runner.visualstudio 2.8.2).
/// </summary>
public abstract class XUnitV2VsTestDiscoveryTests<TContainer> : TestRunnerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Discover_MultipleNamespacesInOneFile_EachAppearsAsItsOwnLeafNamespace()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.XUnitV2MultiNs")
      .WithFramework(TestFrameworkKind.XUnitV2VsTest)
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

  [Fact]
  public async Task Discover_TestClassAndMethod_ReportCorrectSourceLocations()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.XUnitV2Location")
      .WithFramework(TestFrameworkKind.XUnitV2VsTest)
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
      .WithName("Sample.XUnitV2Params")
      .WithFramework(TestFrameworkKind.XUnitV2VsTest)
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
      .WithName("Sample.XUnitV2ComplexArgs")
      .WithFramework(TestFrameworkKind.XUnitV2VsTest)
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
      .WithName("Sample.XUnitV2MemberData")
      .WithFramework(TestFrameworkKind.XUnitV2VsTest)
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
      .WithName("Sample.XUnitV2CustomName")
      .WithFramework(TestFrameworkKind.XUnitV2VsTest)
      .WithFile("CustomName.cs", TestFixtures.XUnitCustomDisplayName)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var method = Assert.Single(NodesOfType(NodeTypeNames.TestMethod));
    Assert.Equal("custom fact name", method.DisplayName);
  }

  [Fact]
  public async Task Discover_FSharpMixedDottedAndPlainFactNames_DiscoversBothMethods()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.FSharpXUnitV2DottedNames")
      .WithFramework(TestFrameworkKind.XUnitV2VsTestFSharp)
      .WithFile("Tests.fs", TestFixtures.FSharpXUnitMixedDottedNames)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var methods = NodesOfType(NodeTypeNames.TestMethod).ToList();
    Assert.True(
      methods.Count == 2,
      $"Expected two F# xUnit facts to be discovered. Node dump:\n{DumpNodes()}");
  }

  [Fact]
  public async Task Run_SlowSuite_ReportsQueuedThenRunningBeforeTerminalStatus()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.XUnitV2Slow")
      .WithFramework(TestFrameworkKind.XUnitV2VsTest)
      .WithFile("SlowTests.cs", TestFixtures.XUnitSlowTests)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var project = Assert.Single(NodesOfType(NodeTypeNames.Project));
    var methods = NodesOfType(NodeTypeNames.TestMethod).ToList();
    Assert.Equal(3, methods.Count);

    await BeginCall(Container.Rpc.TestRunnerRunAsync(project.Id), TimeSpan.FromMinutes(2));

    var methodWithRunning = methods.FirstOrDefault(method =>
      StatusHistory.TryGetValue(method.Id, out var history) &&
      HistoryContainsInOrder(history, "Queued", "Running", "Passed"));

    Assert.NotNull(methodWithRunning);
  }

  [Fact]
  public async Task SyncFile_OnFSharpFile_KeepsNodesAndRunStillReportsResults()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.FSharpXUnitV2SyncRun")
      .WithFramework(TestFrameworkKind.XUnitV2VsTestFSharp)
      .WithFile("Tests.fs", TestFixtures.FSharpXUnitMixedDottedNames)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    Assert.Equal(2, NodesOfType(NodeTypeNames.TestMethod).Count());

    // The Roslyn source locator is C#-only, so syncing an .fs file finds no methods.
    // The server must NOT treat that as "every test was deleted" and wipe the nodes —
    // that regression made a subsequent run bail with nothing to execute.
    var fsPath = Path.Combine(fixture.ProjectDir, "Tests.fs");
    await Container.Rpc.TestRunnerSyncFileAsync(fsPath, TestFixtures.FSharpXUnitMixedDottedNames, version: 1);

    Assert.True(
      NodesOfType(NodeTypeNames.TestMethod).Count() == 2,
      $"syncFile on an .fs file must not remove discovered nodes. Node dump:\n{DumpNodes()}");

    // And a run after the sync must still discover + execute the tests.
    var project = Assert.Single(NodesOfType(NodeTypeNames.Project));
    await BeginCall(Container.Rpc.TestRunnerRunAsync(project.Id), TimeSpan.FromMinutes(2));

    Assert.All(NodesOfType(NodeTypeNames.TestMethod), method =>
      Assert.True(
        LastStatusKind.TryGetValue(method.Id, out var kind) && kind == "Passed",
        $"Expected F# test {method.DisplayName} to pass after syncFile. Node dump:\n{DumpNodes()}"));
  }

  [Fact]
  public async Task Run_ProjectWithFailingTest_ReportsFailedLeafAndAggregatesToParent()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.XUnitV2PassFail")
      .WithFramework(TestFrameworkKind.XUnitV2VsTest)
      .WithFile("Cases.cs", TestFixtures.XUnitPassingAndFailing)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var project = Assert.Single(NodesOfType(NodeTypeNames.Project));
    var testClass = Assert.Single(NodesOfType(NodeTypeNames.TestClass), n => n.DisplayName == "Cases");
    var passes = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "Passes");
    var fails = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "Fails");

    await BeginCall(Container.Rpc.TestRunnerRunAsync(project.Id), TimeSpan.FromMinutes(2));

    Assert.Equal("Passed", LastStatusKind.GetValueOrDefault(passes.Id));
    Assert.Equal("Failed", LastStatusKind.GetValueOrDefault(fails.Id));

    // A single failing child must drag the parent class aggregate to Failed.
    Assert.Equal("Failed", LastStatusKind.GetValueOrDefault(testClass.Id));

    Assert.NotNull(LastRunnerStatus);
    Assert.Equal(1, LastRunnerStatus!.TotalPassed);
    Assert.Equal(1, LastRunnerStatus.TotalFailed);
  }

  [Fact]
  public async Task RunSolution_AfterTestRemovedFromSource_PrunesStaleTestAndRunsSurvivor()
  {
    const string twoMethods = """
      using Xunit;

      namespace Sample.SolutionPrune;

      public class C
      {
          [Fact]
          public void Keep() => Assert.True(true);

          [Fact]
          public void Removed() => Assert.True(true);
      }
      """;

    const string onlyKeep = """
      using Xunit;

      namespace Sample.SolutionPrune;

      public class C
      {
          [Fact]
          public void Keep() => Assert.True(true);
      }
      """;

    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.XUnitV2SolutionPrune")
      .WithFramework(TestFrameworkKind.XUnitV2VsTest)
      .WithFile("Tests.cs", twoMethods)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    // Both tests are discovered from the freshly built DLL at open time.
    var keep = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "Keep");
    Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "Removed");

    // Delete one test from source without re-discovering. The node is now stale: still
    // in the tree (and the on-disk DLL), but gone from source.
    await File.WriteAllTextAsync(Path.Combine(fixture.ProjectDir, "Tests.cs"), onlyKeep);

    // Running the whole solution must rebuild + re-discover, pruning the removed test
    // before the run. Before the fix the solution path skipped discovery, so "Removed"
    // lingered with no status while the run executed the stale node set.
    var solution = Assert.Single(NodesOfType(NodeTypeNames.Solution));
    await BeginCall(Container.Rpc.TestRunnerRunAsync(solution.Id), TimeSpan.FromMinutes(2));

    var survivors = NodesOfType(NodeTypeNames.TestMethod).ToList();
    Assert.True(
      survivors.Count == 1 && survivors[0].DisplayName == "Keep",
      $"Expected only 'Keep' to remain after the solution run. Node dump:\n{DumpNodes()}");
    Assert.Equal("Passed", LastStatusKind.GetValueOrDefault(keep.Id));
  }

  private string DumpNodes() =>
    string.Join("\n", Nodes.Values
      .OrderBy(n => n.Id)
      .Select(n => $"  [{n.Type.Type}] {n.Id} (display='{n.DisplayName}', parent={n.ParentId})"));

  private static bool HistoryContainsInOrder(IEnumerable<string> history, params string[] expected)
  {
    var index = 0;
    foreach (var status in history)
    {
      if (status != expected[index]) continue;
      index++;
      if (index == expected.Length) return true;
    }

    return false;
  }
}

public sealed class XUnitV2VsTestDiscoverySdk10Linux : XUnitV2VsTestDiscoveryTests<Sdk10LinuxContainer>;