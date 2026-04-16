using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Build;

/// <summary>
/// Verifies terminal-mode execution for workspace/build and workspace/build-solution:
///   1. dotnet build command shape and buildArgs passthrough.
///   2. Non-zero process exit surfaces a displayError with exit code.
///   3. workspace/build-solution targets the solution path directly without a picker.
/// </summary>
public abstract class WorkspaceBuildTerminalTests<TContainer> : WorkspaceBuildTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Build_TerminalMode_AppendsBuildArgsAfterTarget()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask = BeginBuild(useTerminal: true, buildArgs: "-c Release -v minimal");
    var job = await ReceiveRunCommandAsync();
    await buildTask;

    Assert.Equal("dotnet", job.Command.Executable);
    Assert.Equal("build", job.Command.Arguments[0]);
    Assert.Contains(job.Command.Arguments, a => a.Contains("AppAlpha.csproj"));
    Assert.Contains("-c Release -v minimal", job.Command.Arguments);

    var targetIndex = job.Command.Arguments.FindIndex(a => a.Contains("AppAlpha.csproj"));
    var buildArgsIndex = job.Command.Arguments.IndexOf("-c Release -v minimal");
    Assert.True(buildArgsIndex > targetIndex, "buildArgs must follow the build target path");
    Assert.Equal(ws.Project("AppAlpha").Dir, job.Command.WorkingDirectory);
  }

  [Fact]
  public async Task Build_TerminalMode_WhenProcessFails_DisplaysErrorWithExitCode()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask = BeginBuild(useTerminal: true);
    _ = await ReceiveRunCommandAsync(exitCode: 23);
    var error = await ReceiveDisplayErrorAsync();
    await buildTask;

    Assert.Equal("Build failed for AppAlpha.csproj (exit code 23)", error);
  }

  [Fact]
  public async Task BuildSolution_TerminalMode_BuildsSolutionWithoutPicker()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("AppAlpha")
      .WithProject("AppBeta")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask = BeginBuildSolution(useTerminal: true, buildArgs: "-p:Deterministic=true");
    var job = await ReceiveRunCommandAsync();
    await buildTask;

    Assert.Equal(0, SelectionCallCount);
    Assert.Equal("dotnet", job.Command.Executable);
    Assert.Equal("build", job.Command.Arguments[0]);
    Assert.Contains(ws.SolutionPath!, job.Command.Arguments);
    Assert.Contains("-p:Deterministic=true", job.Command.Arguments);
    Assert.Equal(Path.GetDirectoryName(ws.SolutionPath!)!, job.Command.WorkingDirectory);
  }
}

public sealed class WorkspaceBuildTerminalSdk10Linux : WorkspaceBuildTerminalTests<Sdk10LinuxContainer>;
