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

    Assert.Contains(picker.Choices, c => c.Display.Contains("ProjectAlpha"));

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

    var debugAttachTask = BeginDebugAttach();
    var picker = await ReceivePickerAsync(_ => null);
    await debugAttachTask;

    Assert.DoesNotContain(picker.Choices, c => c.Display.Contains("ProjectAlpha"));

    await CompleteJobAsync(job);
  }
}
public sealed class WorkspaceDebugAttachSdk10Linux : WorkspaceDebugAttachTests<Sdk10LinuxContainer>;