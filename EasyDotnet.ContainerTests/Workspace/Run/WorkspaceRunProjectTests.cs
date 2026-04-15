using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Run;

/// <summary>
/// Verifies project-picker behaviour in workspace/run:
///   1. A promptSelection reverse request is sent when no default is set.
///   2. The picked project is persisted — proven by a second call with useDefault=true:
///      a. No promptSelection is sent (picker bypassed).
///      b. runCommandManaged receives an identical RunCommand, confirming the same project was run.
///   3. A solution with exactly 1 runnable project skips the picker entirely (auto-selects).
///   4. A persisted project removed from the solution is treated as stale — the server falls
///      back to auto-selecting the remaining project without showing a picker.
///   5. When the user dismisses the project picker (returns null), workspace/run completes
///      cleanly without dispatching runCommandManaged or displaying an error.
///   6. A solution with only non-runnable projects (class libraries) displays an error and
///      never dispatches runCommandManaged or shows a picker.
/// </summary>
public abstract class WorkspaceRunProjectTests<TContainer> : WorkspaceRunTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Run_WithTwoRunnableProjects_PersistsDefaultAndBypassesPickerOnSecondCall()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var runTask1 = BeginRun();

    await ReceiveSelectionAsync(req => req.Choices[0].Id);
    await runTask1;
    var job1 = await ReceiveRunCommandAsync();

    Assert.Equal(1, SelectionCallCount);

    await BeginRun(useDefault: true);

    var job2 = await ReceiveRunCommandAsync();

    Assert.Equal(1, SelectionCallCount);
    Assert.Equal(job1.Command.Executable, job2.Command.Executable);
    Assert.Equal(job1.Command.WorkingDirectory, job2.Command.WorkingDirectory);
    Assert.Equal(job1.Command.Arguments, job2.Command.Arguments);
  }

  [Fact]
  public async Task Run_WithExactlyOneRunnableProject_AutoSelectsWithoutShowingPicker()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .Build();

    await InitializeWorkspaceAsync(ws);

    // No picker must appear — the single project is auto-selected.
    await BeginRun();
    var job = await ReceiveRunCommandAsync();

    Assert.Equal(0, SelectionCallCount);
    Assert.Contains("ProjectAlpha", job.Command.WorkingDirectory);
  }

  [Fact]
  public async Task Run_WithPersistedProjectRemovedFromSolution_ClearsDefaultAndAutoSelectsRemainingProject()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .Build();
    await InitializeWorkspaceAsync(ws);

    // First run: pick ProjectAlpha and persist it as the default.
    var runTask1 = BeginRun();
    await ReceiveSelectionAsync(req =>
      Array.Find(req.Choices, c => c.Display.Contains("ProjectAlpha"))?.Id ?? req.Choices[0].Id);
    await runTask1;
    var job1 = await ReceiveRunCommandAsync();

    Assert.Contains("ProjectAlpha", job1.Command.WorkingDirectory);
    Assert.Equal(1, SelectionCallCount);

    // Remove ProjectAlpha from the solution without deleting it from disk.
    ws.RemoveFromSolution("ProjectAlpha");

    // Second run with useDefault=true: ProjectAlpha is no longer in the solution so the
    // stale default must be cleared. Only ProjectBeta remains — it is auto-selected without
    // showing a picker (single-project fast path in PickAndPersistFromSolutionAsync).
    await BeginRun(useDefault: true);
    var job2 = await ReceiveRunCommandAsync();

    // No new picker — selection count must remain at 1.
    Assert.Equal(1, SelectionCallCount);

    // The run must target ProjectBeta, proving the stale ProjectAlpha default was discarded.
    Assert.Contains("ProjectBeta", job2.Command.WorkingDirectory);
  }
  [Fact]
  public async Task Run_WhenUserDismissesProjectPicker_CompletesCleanlyWithoutRunningAnything()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .Build();
    await InitializeWorkspaceAsync(ws);

    // Dismiss the picker by returning null.
    var runTask = BeginRun();
    await ReceiveSelectionAsync(_ => null);
    await runTask;

    // workspace/run must complete without dispatching a run command or displaying an error.
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called when picker is dismissed");
    Assert.Equal(1, SelectionCallCount);
  }

  [Fact]
  public async Task Run_SolutionWithNoRunnableProjects_DisplaysErrorAndDoesNotDispatchRunCommand()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ClassLibraryAlpha", p => p.AsLibrary())
      .Build();
    await InitializeWorkspaceAsync(ws);

    await BeginRun();

    var error = await ReceiveDisplayErrorAsync();
    Assert.Equal("No runnable projects found in solution", error);
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called when no runnable projects exist");
    Assert.Equal(0, SelectionCallCount);
  }
}

public sealed class WorkspaceRunProjectSdk8Linux : WorkspaceRunProjectTests<Sdk8LinuxContainer>;
public sealed class WorkspaceRunProjectSdk9Linux : WorkspaceRunProjectTests<Sdk9LinuxContainer>;
public sealed class WorkspaceRunProjectSdk10Linux : WorkspaceRunProjectTests<Sdk10LinuxContainer>;