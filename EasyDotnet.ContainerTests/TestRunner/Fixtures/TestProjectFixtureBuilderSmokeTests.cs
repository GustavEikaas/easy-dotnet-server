namespace EasyDotnet.ContainerTests.TestRunner.Fixtures;

/// <summary>
/// Host-only smoke tests for <see cref="TestProjectFixtureBuilder"/>. No container involved —
/// these verify the builder scaffolds a project that actually builds with the pinned MSTest
/// packages, so plumbing failures show up here rather than inside a container test.
/// </summary>
public sealed class TestProjectFixtureBuilderSmokeTests
{
  [Fact]
  public void Build_MsTestVsTest_ProducesRunnableAssembly()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Smoke.MsTest")
      .WithFramework(TestFrameworkKind.MsTestVsTest)
      .WithNamespace("Smoke.MsTest.Alpha", ns => ns
        .WithClass("Class1", c => c.WithTestMethod("Passing")))
      .WithFile("MultiNs.cs", TestFixtures.MsTestBlockNamespaces)
      .WithFile("DataRows.cs", TestFixtures.MsTestDataRowDisplayName)
      .Build();

    Assert.True(File.Exists(fixture.CsprojPath), $"csproj missing: {fixture.CsprojPath}");
    Assert.NotNull(fixture.SolutionPath);
    Assert.True(File.Exists(fixture.SolutionPath), $"slnx missing: {fixture.SolutionPath}");

    var dllPath = Path.Combine(fixture.ProjectDir, "bin", "Debug", "net8.0", "Smoke.MsTest.dll");
    Assert.True(File.Exists(dllPath), $"built assembly missing: {dllPath}");
  }

  [Fact]
  public void Build_WithoutSolution_SkipsSlnx()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Smoke.NoSln")
      .WithFramework(TestFrameworkKind.MsTestVsTest)
      .WithoutSolution()
      .WithNamespace("Smoke.NoSln", ns => ns
        .WithClass("C", c => c.WithTestMethod("M")))
      .Build();

    Assert.Null(fixture.SolutionPath);
  }

  [Fact]
  public void Build_WithoutFramework_Throws()
  {
    var builder = new TestProjectFixtureBuilder().WithName("Smoke.NoFramework");
    Assert.Throws<InvalidOperationException>(() => builder.Build());
  }
}
