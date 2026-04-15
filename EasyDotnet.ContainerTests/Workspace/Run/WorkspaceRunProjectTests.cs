using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Run;

/// <summary>
/// Verifies project-picker behaviour in workspace/run:
///   1. A promptSelection reverse request is sent when no default is set.
///   2. The picked project is persisted — proven by a second call with useDefault=true:
///      a. No promptSelection is sent (picker bypassed).
///      b. runCommandManaged receives an identical RunCommand, confirming the same project was run.
/// </summary>
public abstract class WorkspaceRunProjectTests<TContainer> : WorkspaceRunTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Run_WithTwoRunnableProjects_PersistsDefaultAndBypassesPickerOnSecondCall()
  {
    using var solution = new TempContainerSolution();
    await InitializeWorkspaceAsync(solution);

    var runTask1 = Container.Rpc.WorkspaceRunAsync();

    await ReceiveSelectionAsync(req => req.Choices[0].Id);
    await runTask1;
    var job1 = await ReceiveRunCommandAsync();

    Assert.Equal(1, SelectionCallCount);

    await Container.Rpc.WorkspaceRunAsync(useDefault: true);

    var job2 = await ReceiveRunCommandAsync();

    Assert.Equal(1, SelectionCallCount);
    Assert.Equal(job1.Command.Executable, job2.Command.Executable);
    Assert.Equal(job1.Command.WorkingDirectory, job2.Command.WorkingDirectory);
    Assert.Equal(job1.Command.Arguments, job2.Command.Arguments);
  }

  [Fact]
  public async Task Run_WithPersistedProjectRemovedFromSolution_ClearsDefaultAndAutoSelectsRemainingProject()
  {
    using var solution = new TempContainerSolution();
    await InitializeWorkspaceAsync(solution);

    // First run: pick ProjectAlpha and persist it as the default.
    var runTask1 = Container.Rpc.WorkspaceRunAsync();
    await ReceiveSelectionAsync(req =>
      Array.Find(req.Choices, c => c.Display.Contains("ProjectAlpha"))?.Id ?? req.Choices[0].Id);
    await runTask1;
    var job1 = await ReceiveRunCommandAsync();

    Assert.Contains("ProjectAlpha", job1.Command.WorkingDirectory);
    Assert.Equal(1, SelectionCallCount);

    // Remove ProjectAlpha from the solution without deleting it from disk.
    solution.RemoveProjectFromSolution(solution.Project1Dir);

    // Second run with useDefault=true: ProjectAlpha is no longer in the solution so the
    // stale default must be cleared. Only ProjectBeta remains — it is auto-selected without
    // showing a picker (single-project fast path in PickAndPersistFromSolutionAsync).
    await Container.Rpc.WorkspaceRunAsync(useDefault: true);
    var job2 = await ReceiveRunCommandAsync();

    // No new picker — selection count must remain at 1.
    Assert.Equal(1, SelectionCallCount);

    // The run must target ProjectBeta, proving the stale ProjectAlpha default was discarded.
    Assert.Contains("ProjectBeta", job2.Command.WorkingDirectory);
  }
}

public sealed class WorkspaceRunProjectSdk8Linux : WorkspaceRunProjectTests<Sdk8LinuxContainer>;
public sealed class WorkspaceRunProjectSdk9Linux : WorkspaceRunProjectTests<Sdk9LinuxContainer>;
public sealed class WorkspaceRunProjectSdk10Linux : WorkspaceRunProjectTests<Sdk10LinuxContainer>;