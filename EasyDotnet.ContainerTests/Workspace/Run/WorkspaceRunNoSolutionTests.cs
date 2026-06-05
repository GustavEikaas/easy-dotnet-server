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
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha")
      .WithProject("AppBeta")
      .Build();

    await InitializeWorkspaceAsync(ws);

    // Dismiss the picker — we only want to inspect its contents.
    var runTask = BeginRun();
    var selection = await ReceiveSelectionAsync(_ => null);
    await runTask;

    Assert.Contains(selection.Choices, c => c.Display.Contains("AppAlpha"));
    Assert.Contains(selection.Choices, c => c.Display.Contains("AppBeta"));
  }
}

/// <summary>
/// Verifies workspace/run heuristics when no solution file and no .csproj files are present —
/// only a standalone .cs file. On SDK 10+ the file is converted to a virtual project, compiled,
/// and the resulting binary is run — identical to a normal project run.
/// </summary>
public abstract class WorkspaceRunNoSolutionSingleFileTests<TContainer> : WorkspaceRunTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Run_WithStandaloneCsFileAndNoProjectsOrSolution_RunsAsScript()
  {
    using var ws = new TempWorkspaceBuilder()
      .SingleFileProject("Hello.cs")
      .Build();

    await InitializeWorkspaceAsync(ws);

    await BeginRun(filePath: ws.SingleFilePath);

    var job = await ReceiveRunCommandAsync();

    Assert.True(
      job.Command.Executable.Contains("Hello", StringComparison.OrdinalIgnoreCase)
      || job.Command.Arguments.Any(a => a.EndsWith("Hello.dll", StringComparison.OrdinalIgnoreCase)),
      "Expected the generated single-file output to be used as either the executable or the dotnet exec target.");
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
    using var ws = new TempWorkspaceBuilder()
      .SingleFileProject("Hello.cs")
      .Build();

    await InitializeWorkspaceAsync(ws);

    await BeginRun(filePath: ws.SingleFilePath);

    var errorMessage = await ReceiveDisplayErrorAsync();
    Assert.False(string.IsNullOrWhiteSpace(errorMessage));

    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called on a legacy SDK");
  }
}

/// <summary>
/// Verifies that cliArgs are passed through to the generated run command.
/// </summary>
public abstract class WorkspaceRunNoSolutionSingleFileWithArgsTests<TContainer> : WorkspaceRunTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Run_WithStandaloneCsFileAndCliArgs_PassesThroughArgsSeparatedByDashDash()
  {
    using var ws = new TempWorkspaceBuilder()
      .SingleFileProject("Hello.cs")
      .Build();

    await InitializeWorkspaceAsync(ws);

    await BeginRun(filePath: ws.SingleFilePath, cliArgs: "user-arg1 user-arg2");

    var job = await ReceiveRunCommandAsync();
    var args = job.Command.Arguments;

    var arg1Index = args.IndexOf("user-arg1");
    var arg2Index = args.IndexOf("user-arg2");

    Assert.True(arg1Index >= 0, "Expected user-arg1 in Arguments");
    Assert.True(arg2Index >= 0, "Expected user-arg2 in Arguments");

    Assert.Equal(arg1Index + 1, arg2Index);
  }
}

[Collection(ContainerCollections.Sdk8Linux)]
public sealed class WorkspaceRunNoSolutionProjectSdk8Linux : WorkspaceRunNoSolutionProjectTests<Sdk8LinuxContainer>;
[Collection(ContainerCollections.Sdk9Linux)]
public sealed class WorkspaceRunNoSolutionProjectSdk9Linux : WorkspaceRunNoSolutionProjectTests<Sdk9LinuxContainer>;
[Collection(ContainerCollections.Sdk10Linux)]
public sealed class WorkspaceRunNoSolutionProjectSdk10Linux : WorkspaceRunNoSolutionProjectTests<Sdk10LinuxContainer>;

// Single-file execution requires SDK 10+.
[Collection(ContainerCollections.Sdk10Linux)]
public sealed class WorkspaceRunNoSolutionSingleFileSdk10Linux : WorkspaceRunNoSolutionSingleFileTests<Sdk10LinuxContainer>;
[Collection(ContainerCollections.Sdk10Linux)]
public sealed class WorkspaceRunNoSolutionSingleFileWithArgsSdk10Linux : WorkspaceRunNoSolutionSingleFileWithArgsTests<Sdk10LinuxContainer>;

// On SDK 8/9 the server must display an error and not attempt to run the file.
[Collection(ContainerCollections.Sdk8Linux)]
public sealed class WorkspaceRunNoSolutionSingleFileLegacySdk8Linux : WorkspaceRunNoSolutionSingleFileLegacySdkTests<Sdk8LinuxContainer>;
[Collection(ContainerCollections.Sdk9Linux)]
public sealed class WorkspaceRunNoSolutionSingleFileLegacySdk9Linux : WorkspaceRunNoSolutionSingleFileLegacySdkTests<Sdk9LinuxContainer>;
