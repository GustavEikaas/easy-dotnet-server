using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Run;

/// <summary>
/// Verifies workspace/run heuristics when no solution file is present.
/// The server must discover runnable projects by scanning for .csproj files
/// up to <see cref="WorkspaceProjectResolver.ProjectSearchDepth"/> directories deep.
/// </summary>
public abstract class WorkspaceRunNoSolutionProjectTests<TContainer> : WorkspaceRunTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Run_WithCsprojFilesAndNoSolution_DiscoversBothProjectsInPicker()
  {
    using var workspace = new TempContainerWorkspace();
    workspace.AddProject("AppAlpha");
    workspace.AddProject("AppBeta");

    await InitializeWorkspaceAsync(workspace);

    // Dismiss the picker — we only want to inspect its contents.
    var runTask = Container.Rpc.WorkspaceRunAsync();
    var selection = await ReceiveSelectionAsync(_ => null);
    await runTask;

    Assert.Contains(selection.Choices, c => c.Display.Contains("AppAlpha"));
    Assert.Contains(selection.Choices, c => c.Display.Contains("AppBeta"));
  }
}

/// <summary>
/// Verifies workspace/run heuristics when no solution file and no .csproj files are present —
/// only a standalone .cs file. On SDK 10+ the file must be run directly as a script.
/// </summary>
public abstract class WorkspaceRunNoSolutionSingleFileTests<TContainer> : WorkspaceRunTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Run_WithStandaloneCsFileAndNoProjectsOrSolution_RunsAsScript()
  {
    using var workspace = new TempContainerWorkspace();
    var csFile = workspace.AddStandaloneFile("Hello.cs");

    await InitializeWorkspaceAsync(workspace);

    // No picker is shown — the server resolves directly to a SingleFileTarget and
    // issues runCommandManaged. workspace/run returns before runCommandManaged arrives
    // (it's dispatched from a background task), so we await them independently.
    await Container.Rpc.WorkspaceRunAsync(filePath: csFile);

    var job = await ReceiveRunCommandAsync();

    Assert.Equal("dotnet", job.Command.Executable);
    Assert.Contains(csFile, job.Command.Arguments);
  }
}

/// <summary>
/// Verifies that on SDKs older than 10, a standalone .cs file with no solution or .csproj
/// triggers a <c>displayError</c> notification and does NOT call <c>runCommandManaged</c>.
/// </summary>
public abstract class WorkspaceRunNoSolutionSingleFileLegacySdkTests<TContainer> : WorkspaceRunTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Run_WithStandaloneCsFileAndNoProjectsOrSolution_DisplaysErrorOnLegacySdk()
  {
    using var workspace = new TempContainerWorkspace();
    var csFile = workspace.AddStandaloneFile("Hello.cs");

    await InitializeWorkspaceAsync(workspace);

    await Container.Rpc.WorkspaceRunAsync(filePath: csFile);

    var errorMessage = await ReceiveDisplayErrorAsync();
    Assert.False(string.IsNullOrWhiteSpace(errorMessage));

    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called on a legacy SDK");
  }
}

public sealed class WorkspaceRunNoSolutionProjectSdk8Linux : WorkspaceRunNoSolutionProjectTests<Sdk8LinuxContainer>;
public sealed class WorkspaceRunNoSolutionProjectSdk9Linux : WorkspaceRunNoSolutionProjectTests<Sdk9LinuxContainer>;
public sealed class WorkspaceRunNoSolutionProjectSdk10Linux : WorkspaceRunNoSolutionProjectTests<Sdk10LinuxContainer>;

// Single-file execution requires SDK 10+.
public sealed class WorkspaceRunNoSolutionSingleFileSdk10Linux : WorkspaceRunNoSolutionSingleFileTests<Sdk10LinuxContainer>;

// On SDK 8/9 the server must display an error and not attempt to run the file.
public sealed class WorkspaceRunNoSolutionSingleFileLegacySdk8Linux : WorkspaceRunNoSolutionSingleFileLegacySdkTests<Sdk8LinuxContainer>;
public sealed class WorkspaceRunNoSolutionSingleFileLegacySdk9Linux : WorkspaceRunNoSolutionSingleFileLegacySdkTests<Sdk9LinuxContainer>;