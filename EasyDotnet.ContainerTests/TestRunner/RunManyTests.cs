using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.TestRunner.Fixtures;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// <c>testrunner/runMany</c>: several nodes, possibly across classes and projects, run as
/// one operation that builds and discovers each project once.
/// </summary>
public abstract class RunManyTests<TContainer> : TestRunnerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

  [Theory]
  [InlineData(TestFrameworkKind.XUnitV3Mtp)]
  [InlineData(TestFrameworkKind.XUnitV2VsTest)]
  public async Task RunMany_MethodsFromDifferentClasses_RunsExactlyThoseMethods(TestFrameworkKind framework)
  {
    using var fixture = BuildTwoClassFixture($"Sample.RunManyClasses{framework}", framework);

    await InitializeTestRunnerAsync(fixture);

    var a1 = Method("A1");
    var a2 = Method("A2");
    var b1 = Method("B1");
    var b2 = Method("B2");

    var result = await BeginCall(Container.Rpc.TestRunnerRunManyAsync(a1.Id, b1.Id), RunTimeout);

    Assert.True(result.Success);
    Assert.Equal("Passed", LastStatusKind.GetValueOrDefault(a1.Id));
    Assert.Equal("Passed", LastStatusKind.GetValueOrDefault(b1.Id));
    AssertNeverRan(a2);
    AssertNeverRan(b2);
    Assert.NotNull(LastRunnerStatus);
    Assert.Equal(2, LastRunnerStatus!.TotalPassed);
  }

  [Fact]
  public async Task RunMany_ClassAndOneOfItsMethods_RunsEachMethodOnce()
  {
    using var fixture = BuildTwoClassFixture("Sample.RunManyOverlap", TestFrameworkKind.XUnitV3Mtp);

    await InitializeTestRunnerAsync(fixture);

    var alpha = Assert.Single(NodesOfType(NodeTypeNames.TestClass), n => n.DisplayName == "Alpha");
    var a1 = Method("A1");

    var result = await BeginCall(Container.Rpc.TestRunnerRunManyAsync(alpha.Id, a1.Id), RunTimeout);

    Assert.True(result.Success);
    Assert.Equal("Passed", LastStatusKind.GetValueOrDefault(Method("A2").Id));
    Assert.Single(StatusHistory[a1.Id], s => s == "Passed");
    AssertNeverRan(Method("B1"));
    Assert.Equal(2, LastRunnerStatus!.TotalPassed);
  }

  [Fact]
  public async Task RunMany_MethodsAcrossMtpAndVsTestProjects_RunsBothInOneOperation()
  {
    using var mtp = new TestProjectFixtureBuilder()
      .WithName("Sample.RunManyMtp")
      .WithFramework(TestFrameworkKind.XUnitV3Mtp)
      .WithoutSolution()
      .WithNamespace("Sample.RunManyMtp", ns => ns.WithClass("MtpCases", c => c.WithTestMethod("M1").WithTestMethod("M2")))
      .Build();
    using var vsTest = new TestProjectFixtureBuilder()
      .WithName("Sample.RunManyVsTest")
      .WithFramework(TestFrameworkKind.XUnitV2VsTest)
      .WithoutSolution()
      .WithNamespace("Sample.RunManyVsTest", ns => ns.WithClass("VsTestCases", c => c.WithTestMethod("V1").WithTestMethod("V2")))
      .Build();
    using var solution = MultiProjectSolution.Create(mtp, vsTest);

    await InitializeTestRunnerAsync(solution.RootDir, solution.SolutionPath);

    var m1 = Method("M1");
    var v1 = Method("V1");

    var result = await BeginCall(Container.Rpc.TestRunnerRunManyAsync(m1.Id, v1.Id), RunTimeout);

    Assert.True(result.Success);
    Assert.Equal("Passed", LastStatusKind.GetValueOrDefault(m1.Id));
    Assert.Equal("Passed", LastStatusKind.GetValueOrDefault(v1.Id));
    AssertNeverRan(Method("M2"));
    AssertNeverRan(Method("V2"));
    Assert.Equal(2, LastRunnerStatus!.TotalPassed);
  }

  [Fact]
  public async Task RunMany_WithUnknownId_FailsWithoutRunningAnything()
  {
    using var fixture = BuildTwoClassFixture("Sample.RunManyUnknown", TestFrameworkKind.XUnitV3Mtp);

    await InitializeTestRunnerAsync(fixture);

    var a1 = Method("A1");

    await Assert.ThrowsAsync<RemoteInvocationException>(() =>
      BeginCall(Container.Rpc.TestRunnerRunManyAsync(a1.Id, "does-not-exist"), RunTimeout));

    AssertNeverRan(a1);
  }

  private static TestProjectFixture BuildTwoClassFixture(string name, TestFrameworkKind framework) =>
    new TestProjectFixtureBuilder()
      .WithName(name)
      .WithFramework(framework)
      .WithNamespace(name, ns => ns
        .WithClass("Alpha", c => c.WithTestMethod("A1").WithTestMethod("A2"))
        .WithClass("Beta", c => c.WithTestMethod("B1").WithTestMethod("B2")))
      .Build();

  private TestNodeDto Method(string displayName) =>
    Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == displayName);

  private void AssertNeverRan(TestNodeDto node) =>
    Assert.False(
      LastStatusKind.TryGetValue(node.Id, out var kind) && kind is "Queued" or "Running" or "Passed" or "Failed",
      $"{node.DisplayName} is outside the run set but saw status '{kind}'.");
}

public sealed class RunManySdk10Linux : RunManyTests<Sdk10LinuxContainer>;