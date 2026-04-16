using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Build;

/// <summary>
/// Verifies solution-mode project selection and default persistence for workspace/build:
///   1. Picker contains Solution + all solution projects.
///   2. Selected project is persisted and reused with useDefault=true.
///   3. A persisted project removed from solution is treated as stale and picker is shown again.
///   4. A persisted project missing on disk is treated as stale and picker is shown again.
///   5. Solutions with no projects display an error and do not dispatch runCommandManaged.
/// </summary>
public abstract class WorkspaceBuildProjectTests<TContainer> : WorkspaceBuildTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private static string PickByDisplayContains(TestPromptSelectionRequest req, string marker) =>
    Array.Find(req.Choices, c => c.Display.Contains(marker))?.Id ?? req.Choices[0].Id;

  [Fact]
  public async Task Build_WithTwoProjects_ShowsSolutionAndProjectsInPicker()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask = BeginBuild(useTerminal: true);
    var selection = await ReceiveSelectionAsync(_ => null);
    await buildTask;

    Assert.Contains(selection.Choices, c => c.Display == "Solution");
    Assert.Contains(selection.Choices, c => c.Display.Contains("ProjectAlpha"));
    Assert.Contains(selection.Choices, c => c.Display.Contains("ProjectBeta"));
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called when picker is dismissed");
    Assert.Equal(1, SelectionCallCount);
  }

  [Fact]
  public async Task Build_WithTwoProjects_PersistsDefaultAndBypassesPickerOnSecondCall()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask1 = BeginBuild(useTerminal: true);
    await ReceiveSelectionAsync(req => PickByDisplayContains(req, "ProjectAlpha"));
    var job1 = await ReceiveRunCommandAsync();
    await buildTask1;
    Assert.Equal(1, SelectionCallCount);

    var buildTask2 = BeginBuild(useDefault: true, useTerminal: true);
    var job2 = await ReceiveRunCommandAsync();
    await buildTask2;

    Assert.Equal(1, SelectionCallCount);
    Assert.Equal(job1.Command.Arguments, job2.Command.Arguments);
    Assert.Equal(job1.Command.WorkingDirectory, job2.Command.WorkingDirectory);
  }

  [Fact]
  public async Task Build_WithPersistedProjectRemovedFromSolution_ClearsDefaultAndShowsPickerAgain()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask1 = BeginBuild(useTerminal: true);
    await ReceiveSelectionAsync(req => PickByDisplayContains(req, "ProjectAlpha"));
    var job1 = await ReceiveRunCommandAsync();
    await buildTask1;

    Assert.Contains("ProjectAlpha", job1.Command.WorkingDirectory);
    Assert.Equal(1, SelectionCallCount);

    ws.RemoveFromSolution("ProjectAlpha");

    var buildTask2 = BeginBuild(useDefault: true, useTerminal: true);
    await ReceiveSelectionAsync(req => PickByDisplayContains(req, "ProjectBeta"));
    var job2 = await ReceiveRunCommandAsync();
    await buildTask2;

    Assert.Equal(2, SelectionCallCount);
    Assert.Contains("ProjectBeta", job2.Command.WorkingDirectory);
  }

  [Fact]
  public async Task Build_WithPersistedProjectMissingOnDisk_ClearsDefaultAndShowsPickerAgain()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask1 = BeginBuild(useTerminal: true);
    await ReceiveSelectionAsync(req => PickByDisplayContains(req, "ProjectAlpha"));
    var job1 = await ReceiveRunCommandAsync();
    await buildTask1;

    Assert.Contains("ProjectAlpha", job1.Command.WorkingDirectory);
    Assert.Equal(1, SelectionCallCount);

    File.Delete(ws.Project("ProjectAlpha").CsprojPath);

    var buildTask2 = BeginBuild(useDefault: true, useTerminal: true);
    await ReceiveSelectionAsync(req => PickByDisplayContains(req, "ProjectBeta"));
    var job2 = await ReceiveRunCommandAsync();
    await buildTask2;

    Assert.Equal(2, SelectionCallCount);
    Assert.Contains("ProjectBeta", job2.Command.WorkingDirectory);
  }

  [Fact]
  public async Task Build_SolutionWithNoProjects_DisplaysErrorAndDoesNotDispatchRunCommand()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask = BeginBuild(useTerminal: true);
    var error = await ReceiveDisplayErrorAsync();
    await buildTask;

    Assert.Equal("No projects found in solution", error);
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called when no projects exist");
    Assert.Equal(0, SelectionCallCount);
  }
}

public sealed class WorkspaceBuildProjectSdk10Linux : WorkspaceBuildProjectTests<Sdk10LinuxContainer>;
