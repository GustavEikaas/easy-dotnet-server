using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.DebugAttach;

/// <summary>
/// Container tests for the <c>workspace/debug-attach</c> command.
///
/// Tests verify:
///   1. Calling debug-attach with no running processes displays an error.
///   2. Calling debug-attach while a process is running shows a picker entry
///      containing the project name and injected PID.
///   3. Calling debug-attach after a process has exited displays an error.
/// </summary>
public abstract class WorkspaceDebugAttachTests<TContainer> : WorkspaceDebugAttachTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task DebugAttach_WithNoRunningProcesses_DisplaysError()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .Build();
    await InitializeWorkspaceAsync(ws);

    await BeginDebugAttach();

    var error = await ReceiveDisplayErrorAsync();
    Assert.Contains("No running processes", error);
  }

  [Fact]
  public async Task DebugAttach_WithRunningProcess_ShowsPickerWithProjectNameAndPid()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .Build();
    await InitializeWorkspaceAsync(ws);

    await BeginRun();
    var job = await ReceiveRunJobAsync();

    var debugAttachTask = BeginDebugAttach();
    var picker = await ReceivePickerAsync(_ => null);
    await debugAttachTask;

    Assert.Single(picker.Choices);
    Assert.Contains("ProjectAlpha", picker.Choices[0].Display);
    Assert.Contains(FakePid.ToString(), picker.Choices[0].Display);

    await CompleteJobAsync(job);
  }

  [Fact]
  public async Task DebugAttach_AfterProcessExits_DisplaysError()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .Build();
    await InitializeWorkspaceAsync(ws);

    await BeginRun();
    var job = await ReceiveRunJobAsync();

    await CompleteJobAsync(job);

    await Task.Delay(500);

    await BeginDebugAttach();

    var error = await ReceiveDisplayErrorAsync();
    Assert.Contains("No running processes", error);
  }
}

public sealed class WorkspaceDebugAttachSdk8Linux : WorkspaceDebugAttachTests<Sdk8LinuxContainer>;
public sealed class WorkspaceDebugAttachSdk9Linux : WorkspaceDebugAttachTests<Sdk9LinuxContainer>;
public sealed class WorkspaceDebugAttachSdk10Linux : WorkspaceDebugAttachTests<Sdk10LinuxContainer>;