using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Test;

/// <summary>
/// Verifies workspace/test heuristics when no solution file is present.
/// The server scans for .csproj files and evaluates them to find test projects.
///   C1. A single test project is auto-selected — no picker is shown.
///   C2. Multiple test projects show a picker with all options.
///   C3. .csproj files found but none are test projects → displayError "No test projects found".
///   C4. No .csproj files at all → displayError "No project files found".
/// </summary>
public abstract class WorkspaceTestNoSolutionTests<TContainer> : WorkspaceTestTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Test_SingleTestProjectNoSolution_AutoSelectsWithoutPicker()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTest();
    var job = await ReceiveRunCommandAsync();
    await testTask;

    Assert.Equal(0, SelectionCallCount);
    Assert.Equal("dotnet", job.Command.Executable);
    Assert.Equal("test", job.Command.Arguments[0]);
    Assert.Contains(job.Command.Arguments, a => a.Contains("TestAlpha.csproj"));
  }

  [Fact]
  public async Task Test_MultipleTestProjectsNoSolution_ShowsPicker()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .WithProject("TestBeta", p => p.AsVsTestProject())
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTest();
    var selection = await ReceiveSelectionAsync(_ => null);
    await testTask;

    Assert.Contains(selection.Choices, c => c.Display.Contains("TestAlpha"));
    Assert.Contains(selection.Choices, c => c.Display.Contains("TestBeta"));
    Assert.Equal(1, SelectionCallCount);
  }

  [Fact]
  public async Task Test_NoTestProjectsFoundNoSolution_DisplaysError()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTest();
    var error = await ReceiveDisplayErrorAsync();
    await testTask;

    Assert.Equal("No test projects found", error);
    Assert.True(RunCommandNotReceived());
    Assert.Equal(0, SelectionCallCount);
  }

  [Fact]
  public async Task Test_NoCsprojFilesNoSolution_DisplaysError()
  {
    using var ws = new TempWorkspaceBuilder()
      .SingleFileProject("Scripts/Hello.cs")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTest();
    var error = await ReceiveDisplayErrorAsync();
    await testTask;

    Assert.Equal("No project files found", error);
    Assert.True(RunCommandNotReceived());
    Assert.Equal(0, SelectionCallCount);
  }
}

public sealed class WorkspaceTestNoSolutionSdk10Linux : WorkspaceTestNoSolutionTests<Sdk10LinuxContainer>;