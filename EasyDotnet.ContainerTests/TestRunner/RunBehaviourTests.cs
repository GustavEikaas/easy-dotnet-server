using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.TestRunner.Fixtures;

namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// Pins what <c>testrunner/run</c> does for each kind of node, across the MTP and VSTest
/// adapters: which leaves run, what the call returns, what <c>testrunner/getResults</c>
/// exposes afterwards and how build failures surface.
/// </summary>
public abstract class RunBehaviourTests<TContainer> : TestRunnerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

  [Theory]
  [InlineData(TestFrameworkKind.XUnitV3Mtp)]
  [InlineData(TestFrameworkKind.XUnitV2VsTest)]
  public async Task Run_TestClass_RunsThatClassAndLeavesOtherClassesUntouched(TestFrameworkKind framework)
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName($"Sample.RunClass{framework}")
      .WithFramework(framework)
      .WithNamespace("Sample.RunClass", ns => ns
        .WithClass("Alpha", c => c.WithTestMethod("A1").WithTestMethod("A2"))
        .WithClass("Beta", c => c.WithTestMethod("B1")))
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var alpha = Assert.Single(NodesOfType(NodeTypeNames.TestClass), n => n.DisplayName == "Alpha");
    var alphaMethods = Children(alpha.Id).ToList();
    Assert.Equal(2, alphaMethods.Count);
    var b1 = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "B1");

    var result = await BeginCall(Container.Rpc.TestRunnerRunWithResultAsync(alpha.Id), RunTimeout);

    Assert.True(result.Success);
    Assert.All(alphaMethods, m => Assert.Equal("Passed", LastStatusKind.GetValueOrDefault(m.Id)));
    Assert.Equal("Passed", LastStatusKind.GetValueOrDefault(alpha.Id));
    Assert.False(
      LastStatusKind.TryGetValue(b1.Id, out var kind) && kind is "Queued" or "Running" or "Passed" or "Failed",
      $"B1 is outside the run set but saw status '{kind}'.");
    Assert.False((await Container.Rpc.TestRunnerGetResultsAsync(b1.Id)).Found);
  }

  [Theory]
  [InlineData(TestFrameworkKind.XUnitV3Mtp)]
  [InlineData(TestFrameworkKind.XUnitV2VsTest)]
  public async Task Run_ProjectWithFailingTest_ReturnsUnsuccessfulAndExposesFailureDetails(TestFrameworkKind framework)
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName($"Sample.RunDetails{framework}")
      .WithFramework(framework)
      .WithFile("Cases.cs", TestFixtures.XUnitPassingAndFailing)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var project = Assert.Single(NodesOfType(NodeTypeNames.Project));
    var passes = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "Passes");
    var fails = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "Fails");

    var result = await BeginCall(Container.Rpc.TestRunnerRunWithResultAsync(project.Id), RunTimeout);

    Assert.False(result.Success);

    var failure = await Container.Rpc.TestRunnerGetResultsAsync(fails.Id);
    Assert.True(failure.Found);
    Assert.NotNull(failure.ErrorMessage);
    Assert.NotEmpty(failure.ErrorMessage!);
    Assert.NotNull(failure.Frames);
    Assert.Contains(failure.Frames!, f => f.IsUserCode && f.File is not null && f.File.EndsWith("Cases.cs", StringComparison.Ordinal));

    var success = await Container.Rpc.TestRunnerGetResultsAsync(passes.Id);
    Assert.True(success.Found);
    Assert.True(success.ErrorMessage is null or { Length: 0 });
  }

  [Theory]
  [InlineData(NodeTypeNames.Project)]
  [InlineData(NodeTypeNames.Solution)]
  public async Task Run_AfterSourceStopsCompiling_ReportsBuildFailedAndRunsNothing(string nodeType)
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName($"Sample.RunBroken{nodeType}")
      .WithFramework(TestFrameworkKind.XUnitV3Mtp)
      .WithFile("Cases.cs", TestFixtures.XUnitPassingAndFailing)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var project = Assert.Single(NodesOfType(NodeTypeNames.Project));
    var target = Assert.Single(NodesOfType(nodeType));
    var methods = NodesOfType(NodeTypeNames.TestMethod).ToList();

    await File.WriteAllTextAsync(Path.Combine(fixture.ProjectDir, "Broken.cs"), "this is not C#");

    var result = await BeginCall(Container.Rpc.TestRunnerRunWithResultAsync(target.Id), RunTimeout);

    Assert.False(result.Success);
    Assert.Equal("BuildFailed", LastStatusKind.GetValueOrDefault(project.Id));
    Assert.All(methods, m => Assert.False(
      LastStatusKind.TryGetValue(m.Id, out var kind) && kind is "Queued" or "Running" or "Passed" or "Failed",
      $"{m.DisplayName} must not run when the build fails, but saw status '{kind}'."));
  }

  [Fact]
  public async Task Run_SolutionWithMtpAndVsTestProjects_RunsEveryProjectWithItsOwnAdapter()
  {
    using var mtp = new TestProjectFixtureBuilder()
      .WithName("Sample.MixedMtp")
      .WithFramework(TestFrameworkKind.XUnitV3Mtp)
      .WithoutSolution()
      .WithNamespace("Sample.MixedMtp", ns => ns.WithClass("MtpCases", c => c.WithTestMethod("M1")))
      .Build();
    using var vsTest = new TestProjectFixtureBuilder()
      .WithName("Sample.MixedVsTest")
      .WithFramework(TestFrameworkKind.XUnitV2VsTest)
      .WithoutSolution()
      .WithNamespace("Sample.MixedVsTest", ns => ns.WithClass("VsTestCases", c => c.WithTestMethod("V1")))
      .Build();
    using var solution = MultiProjectSolution.Create(mtp, vsTest);

    await InitializeTestRunnerAsync(solution.RootDir, solution.SolutionPath);

    Assert.Equal(2, NodesOfType(NodeTypeNames.Project).Count());
    var m1 = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "M1");
    var v1 = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "V1");
    var solutionNode = Assert.Single(NodesOfType(NodeTypeNames.Solution));

    var result = await BeginCall(Container.Rpc.TestRunnerRunWithResultAsync(solutionNode.Id), RunTimeout);

    Assert.True(result.Success);
    Assert.Equal("Passed", LastStatusKind.GetValueOrDefault(m1.Id));
    Assert.Equal("Passed", LastStatusKind.GetValueOrDefault(v1.Id));
    Assert.NotNull(LastRunnerStatus);
    Assert.Equal(2, LastRunnerStatus!.TotalPassed);
    Assert.Equal(0, LastRunnerStatus.TotalFailed);
  }
}

public sealed class RunBehaviourSdk10Linux : RunBehaviourTests<Sdk10LinuxContainer>;