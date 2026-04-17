using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Build;

/// <summary>
/// Verifies workspace/build behavior when no solution file is present:
///   1. A single .csproj is auto-selected.
///   2. Multiple .csproj files are shown in a picker.
///   3. No .csproj files shows a user-facing error.
/// </summary>
public abstract class WorkspaceBuildNoSolutionTests<TContainer> : WorkspaceBuildTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Build_WithSingleProjectAndNoSolution_AutoSelectsWithoutPicker()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask = BeginBuild(useTerminal: true);
    var job = await ReceiveRunCommandAsync();
    await buildTask;

    Assert.Equal(0, SelectionCallCount);
    Assert.Equal("dotnet", job.Command.Executable);
    Assert.Equal("build", job.Command.Arguments[0]);
    Assert.Contains(job.Command.Arguments, a => a.Contains("AppAlpha.csproj"));
  }

  [Fact]
  public async Task Build_WithMultipleProjectsAndNoSolution_ShowsPicker()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha")
      .WithProject("AppBeta")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask = BeginBuild(useTerminal: true);
    var selection = await ReceiveSelectionAsync(_ => null);
    await buildTask;

    Assert.Contains(selection.Choices, c => c.Display.Contains("AppAlpha"));
    Assert.Contains(selection.Choices, c => c.Display.Contains("AppBeta"));
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called when picker is dismissed");
    Assert.Equal(1, SelectionCallCount);
  }

  [Fact]
  public async Task Build_WithNoProjectsAndNoSolution_DisplaysError()
  {
    using var ws = new TempWorkspaceBuilder()
      .SingleFileProject("Scripts/Hello.cs")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask = BeginBuild(useTerminal: true);
    var error = await ReceiveDisplayErrorAsync();
    await buildTask;

    Assert.Equal("No project files found", error);
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called when no projects exist");
    Assert.Equal(0, SelectionCallCount);
  }
}

public sealed class WorkspaceBuildNoSolutionSdk10Linux : WorkspaceBuildNoSolutionTests<Sdk10LinuxContainer>;