using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Dap;

public abstract class BreakpointCandidateTests<TContainer> : ContainerTestBase<TContainer>
    where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task GetCandidates_OnLinqChain_ReturnsMultipleValidColumns()
  {
    var code = string.Join("\n",
    [
            "using System.Linq;",
            "class C {",
            "    void M() {",
            "        var items = new[] { 1, 2, 3 };",
            "        var res = items.Where(x => x > 1).Select(y => y * 2); //breakpoint on this line",
            "    }",
            "}"
        ]);

    using var ws = new TempWorkspaceBuilder()
        .SingleFileProject("Program.cs", code)
        .Build();

    await InitializeWorkspaceAsync(ws);

    var filePath = $"{ws.RootDir}/Program.cs";

    var candidates = await Container.Rpc.DapBreakpointCandidatesAsync(filePath, 4);

    Assert.NotNull(candidates);
    Assert.Equal(3, candidates.Count);
    Assert.All(candidates, c => Assert.Equal(4, c.Line));
    Assert.Contains(candidates, c => c.TargetText.Contains("var res = items.Where"));
    Assert.Contains(candidates, c => c.TargetText.Contains("x > 1"));
    Assert.Contains(candidates, c => c.TargetText.Contains("y * 2"));
  }

  [Fact]
  public async Task GetCandidates_OnEmptyLine_SnapsToNextValidStatement()
  {
    var code = string.Join("\n",
    [
            "class C {",
            "    void M() {",
            "",
            "        int x = 5;",
            "    }",
            "}"
        ]);

    using var ws = new TempWorkspaceBuilder()
        .SingleFileProject("Program.cs", code)
        .Build();

    await InitializeWorkspaceAsync(ws);
    var filePath = $"{ws.RootDir}/Program.cs";

    var candidates = await Container.Rpc.DapBreakpointCandidatesAsync(filePath, 2);

    Assert.Single(candidates);
    Assert.Equal(3, candidates[0].Line);
    Assert.Contains("int x = 5;", candidates[0].TargetText);
  }

  [Fact]
  public async Task GetCandidates_OnMultipleStatements_ReturnsAllStatements()
  {
    var code = string.Join("\n",
    [
            "class C {",
            "    void M() {",
            "        int i = 0; int x = 2; int y = 3;", // Line 2
            "    }",
            "}"
        ]);

    using var ws = new TempWorkspaceBuilder()
        .SingleFileProject("Program.cs", code)
        .Build();

    await InitializeWorkspaceAsync(ws);
    var filePath = $"{ws.RootDir}/Program.cs";

    var candidates = await Container.Rpc.DapBreakpointCandidatesAsync(filePath, 2);

    Assert.Equal(3, candidates.Count);
    Assert.All(candidates, c => Assert.Equal(2, c.Line));
    Assert.Contains(candidates, c => c.TargetText.Contains("int i = 0;"));
    Assert.Contains(candidates, c => c.TargetText.Contains("int x = 2;"));
    Assert.Contains(candidates, c => c.TargetText.Contains("int y = 3;"));
  }

  [Fact]
  public async Task GetCandidates_OutOfBoundsLine_ReturnsEmptyList()
  {
    var code = string.Join("\n",
    [
            "class C {",
            "    void M() { }",
            "}"
        ]);

    using var ws = new TempWorkspaceBuilder()
        .SingleFileProject("Program.cs", code)
        .Build();

    await InitializeWorkspaceAsync(ws);
    var filePath = $"{ws.RootDir}/Program.cs";

    var candidates = await Container.Rpc.DapBreakpointCandidatesAsync(filePath, 100);

    Assert.NotNull(candidates);
    Assert.Empty(candidates);
  }
}

public sealed class BreakpointCandidateSdk10Linux : BreakpointCandidateTests<Sdk10LinuxContainer>;