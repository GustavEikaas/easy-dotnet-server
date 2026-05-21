using EasyDotnet.ContainerTests;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Clean;

/// <summary>
/// Verifies workspace/clean behavior:
///   1. Solution workspaces always prompt with Solution + all projects and never persist a default.
///   2. Clean executes through BuildHost (no runCommandManaged).
///   3. No-solution workspaces auto-select a single project.
///   4. The solution itself can be selected as the clean target.
/// </summary>
public abstract class WorkspaceCleanTests<TContainer> : WorkspaceCleanTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private static string PickByDisplayContains(TestPromptSelectionRequest req, string marker) =>
    Array.Find(req.Choices, c => c.Display.Contains(marker))?.Id ?? req.Choices[0].Id;

  private static void AssertBuildOutputsExist(TempProject project)
  {
    Assert.True(File.Exists(project.OutputDllPath()), $"Expected clean precondition output to exist: {project.OutputDllPath()}");
    Assert.True(File.Exists(project.IntermediateOutputDllPath()), $"Expected clean precondition output to exist: {project.IntermediateOutputDllPath()}");
  }

  private static void AssertBuildOutputsCleaned(TempProject project)
  {
    Assert.False(File.Exists(project.OutputDllPath()), $"Expected clean to remove: {project.OutputDllPath()}");
    Assert.False(File.Exists(project.IntermediateOutputDllPath()), $"Expected clean to remove: {project.IntermediateOutputDllPath()}");
  }

  [Fact]
  public async Task Clean_WithTwoProjects_ShowsSolutionAndProjectsEveryTime()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var cleanTask1 = BeginClean();
    var selection1 = await ReceiveSelectionAsync(req => PickByDisplayContains(req, "ProjectAlpha"));
    var message1 = await ReceiveDisplayMessageAsync();
    await cleanTask1;

    Assert.Contains(selection1.Choices, c => c.Display == "Solution");
    Assert.Contains(selection1.Choices, c => c.Display.Contains("ProjectAlpha"));
    Assert.Contains(selection1.Choices, c => c.Display.Contains("ProjectBeta"));
    Assert.Equal("Clean succeeded.", message1);
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called for BuildHost-backed clean");

    var cleanTask2 = BeginClean();
    var selection2 = await ReceiveSelectionAsync(_ => null);
    await cleanTask2;

    Assert.Contains(selection2.Choices, c => c.Display == "Solution");
    Assert.Contains(selection2.Choices, c => c.Display.Contains("ProjectAlpha"));
    Assert.Contains(selection2.Choices, c => c.Display.Contains("ProjectBeta"));
    Assert.Equal(2, SelectionCallCount);
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called when the clean picker is dismissed");
  }

  [Fact]
  public async Task Clean_WithSingleProjectAndNoSolution_AutoSelectsWithoutPicker()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha")
      .Build();
    await InitializeWorkspaceAsync(ws);
    var project = ws.Project("AppAlpha");

    var buildTask = BeginBuild();
    var buildMessage = await ReceiveDisplayMessageAsync();
    await buildTask;

    Assert.Equal("Build succeeded.", buildMessage);
    AssertBuildOutputsExist(project);

    var cleanTask = BeginClean();
    var message = await ReceiveDisplayMessageAsync();
    await cleanTask;

    Assert.Equal(0, SelectionCallCount);
    Assert.Equal("Clean succeeded.", message);
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called for BuildHost-backed clean");
    Assert.True(QuickFixSetNotReceived(), "quickfix/set must not be sent for clean success");
    Assert.True(QuickFixSetSilentNotReceived(), "quickfix/set-silent must not be sent for clean success");
    AssertBuildOutputsCleaned(project);
  }

  [Fact]
  public async Task Clean_WhenSolutionIsSelected_CleansSolutionTarget()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("AppAlpha")
      .Build();
    await InitializeWorkspaceAsync(ws);
    var project = ws.Project("AppAlpha");

    var buildTask = BeginBuildSolution();
    var buildMessage = await ReceiveDisplayMessageAsync();
    await buildTask;

    Assert.Equal("Build succeeded.", buildMessage);
    AssertBuildOutputsExist(project);

    var cleanTask = BeginClean();
    var selection = await ReceiveSelectionAsync(req => PickByDisplayContains(req, "Solution"));
    var message = await ReceiveDisplayMessageAsync();
    await cleanTask;

    Assert.Contains(selection.Choices, c => c.Display == "Solution");
    Assert.Equal("Clean succeeded.", message);
    Assert.Equal(1, SelectionCallCount);
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called for BuildHost-backed clean");
    AssertBuildOutputsCleaned(project);
  }
}

public sealed class WorkspaceCleanSdk10Linux : WorkspaceCleanTests<Sdk10LinuxContainer>;