using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.TestRunner.Fixtures;

namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// <c>testrunner/syncFile</c> tracks edits to an already-discovered file and returns
/// the new 0-based positions so the client can move test signs without re-discovering.
/// TUnit (MTP) is used as the vehicle; the endpoint itself is framework-agnostic
/// (it re-parses the file via Roslyn), so passing here gives coverage for every
/// framework whose discovery populates <c>FilePath</c> on TestClass/TestMethod nodes.
/// </summary>
public abstract class TUnitSyncFileTests<TContainer> : TestRunnerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task SyncFile_AfterInsertingLeadingBlankLines_UpdatesClassAndMethodPositions()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.TUnitSyncFile")
      .WithFramework(TestFrameworkKind.TUnitMtp)
      .WithFile("Loc.cs", TestFixtures.TUnitLocationMarker)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var locPath = Path.Combine(fixture.ProjectDir, "Loc.cs");
    var testClass = Assert.Single(NodesOfType(NodeTypeNames.TestClass), n => n.DisplayName == "C");
    var method = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "M");

    Assert.Equal(TestFixtures.TUnitLocationLines.ClassSignature, testClass.SignatureLine);
    Assert.Equal(TestFixtures.TUnitLocationLines.MethodSignature, method.SignatureLine);

    const int shift = 3;
    var shifted = string.Concat(Enumerable.Repeat("\n", shift)) + TestFixtures.TUnitLocationMarker;

    var result = await Container.Rpc.TestRunnerSyncFileAsync(locPath, shifted, version: 7);

    Assert.Equal(7, result.Version);

    var classUpdate = Assert.Single(result.Updates, u => u.Id == testClass.Id);
    Assert.Equal(TestFixtures.TUnitLocationLines.ClassSignature + shift, classUpdate.SignatureLine);
    Assert.Equal(TestFixtures.TUnitLocationLines.ClassBodyStart + shift, classUpdate.BodyStartLine);
    Assert.Equal(TestFixtures.TUnitLocationLines.ClassEnd + shift, classUpdate.EndLine);

    var methodUpdate = Assert.Single(result.Updates, u => u.Id == method.Id);
    Assert.Equal(TestFixtures.TUnitLocationLines.MethodSignature + shift, methodUpdate.SignatureLine);
    Assert.Equal(TestFixtures.TUnitLocationLines.MethodBodyStart + shift, methodUpdate.BodyStartLine);
    Assert.Equal(TestFixtures.TUnitLocationLines.MethodEnd + shift, methodUpdate.EndLine);
  }

  [Fact]
  public async Task SyncFile_TracksSubcasesUnderTheoryGroup_WhenParameterisedMethodMoves()
  {
    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.TUnitSyncFileParams")
      .WithFramework(TestFrameworkKind.TUnitMtp)
      .WithFile("Rows.cs", TestFixtures.TUnitParameterized)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var rowsPath = Path.Combine(fixture.ProjectDir, "Rows.cs");
    var theoryGroup = Assert.Single(NodesOfType(NodeTypeNames.TheoryGroup), n => n.DisplayName == "M");
    var subcases = Children(theoryGroup.Id)
      .Where(n => n.Type.Type == NodeTypeNames.Subcase)
      .ToList();
    Assert.NotEmpty(subcases);

    var originalMethodSig = theoryGroup.SignatureLine
      ?? throw new Xunit.Sdk.XunitException("Expected TheoryGroup to have a signature line after discovery.");

    const int shift = 5;
    var shifted = string.Concat(Enumerable.Repeat("\n", shift)) + TestFixtures.TUnitParameterized;

    var result = await Container.Rpc.TestRunnerSyncFileAsync(rowsPath, shifted, version: 42);

    Assert.Equal(42, result.Version);

    var groupUpdate = Assert.Single(result.Updates, u => u.Id == theoryGroup.Id);
    Assert.Equal(originalMethodSig + shift, groupUpdate.SignatureLine);

    foreach (var subcase in subcases)
    {
      var subcaseUpdate = Assert.Single(result.Updates, u => u.Id == subcase.Id);
      Assert.Equal(originalMethodSig + shift, subcaseUpdate.SignatureLine);
    }
  }

  [Fact]
  public async Task SyncFile_AfterRemovingOneMethod_DoesNotEmitUpdateForGoneNode()
  {
    const string twoMethods = """
      namespace Sample.SyncRemove;

      public class C
      {
          [TUnit.Core.Test]
          public async Task M() { await Task.CompletedTask; }

          [TUnit.Core.Test]
          public async Task N() { await Task.CompletedTask; }
      }
      """;

    const string onlyM = """
      namespace Sample.SyncRemove;

      public class C
      {
          [TUnit.Core.Test]
          public async Task M() { await Task.CompletedTask; }
      }
      """;

    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.TUnitSyncRemove")
      .WithFramework(TestFrameworkKind.TUnitMtp)
      .WithFile("Two.cs", twoMethods)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var m = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), n => n.DisplayName == "M");
    var n = Assert.Single(NodesOfType(NodeTypeNames.TestMethod), x => x.DisplayName == "N");

    var path = Path.Combine(fixture.ProjectDir, "Two.cs");
    var result = await Container.Rpc.TestRunnerSyncFileAsync(path, onlyM, version: 1);

    Assert.Contains(result.Updates, u => u.Id == m.Id);
    Assert.DoesNotContain(result.Updates, u => u.Id == n.Id);
  }

  [Fact]
  public async Task SyncFile_WhenFileGainsNewTestMethod_SurfacesItAsProbableTest()
  {
    const string original = """
      namespace Sample.SyncNew;

      public class C
      {
          [TUnit.Core.Test]
          public async Task M() { await Task.CompletedTask; }
      }
      """;

    const string withAddedTest = """
      namespace Sample.SyncNew;

      public class C
      {
          [TUnit.Core.Test]
          public async Task M() { await Task.CompletedTask; }

          [TUnit.Core.Test]
          public async Task JustWrittenByUser() { await Task.CompletedTask; }
      }
      """;

    using var fixture = new TestProjectFixtureBuilder()
      .WithName("Sample.TUnitSyncNew")
      .WithFramework(TestFrameworkKind.TUnitMtp)
      .WithFile("New.cs", original)
      .Build();

    await InitializeTestRunnerAsync(fixture);

    var path = Path.Combine(fixture.ProjectDir, "New.cs");
    var result = await Container.Rpc.TestRunnerSyncFileAsync(path, withAddedTest, version: 1);

    Assert.Contains(result.Updates, u => u.Id.Contains("JustWrittenByUser"));

    var probableNode = Assert.Single(NodesOfType(NodeTypeNames.ProbableTest),
        n => n.DisplayName == "JustWrittenByUser");
    Assert.Equal(NodeTypeNames.ProbableTest, probableNode.Type.Type);
    Assert.Contains("Run", probableNode.AvailableActions ?? []);
    Assert.Contains("Debug", probableNode.AvailableActions ?? []);
  }
}

public sealed class TUnitSyncFileSdk10Linux : TUnitSyncFileTests<Sdk10LinuxContainer>;
