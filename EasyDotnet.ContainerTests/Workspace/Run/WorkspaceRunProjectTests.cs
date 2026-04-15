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
}

public sealed class WorkspaceRunProjectSdk8Linux : WorkspaceRunProjectTests<Sdk8LinuxContainer>;
public sealed class WorkspaceRunProjectSdk9Linux : WorkspaceRunProjectTests<Sdk9LinuxContainer>;
public sealed class WorkspaceRunProjectSdk10Linux : WorkspaceRunProjectTests<Sdk10LinuxContainer>;