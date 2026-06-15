using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Run;

/// <summary>
/// Verifies that a project declared as a class library (<c>OutputType=Library</c>) but with static
/// <c>RunCommand</c>/<c>RunArguments</c> MSBuild properties is treated as a runnable project:
///   1. workspace/run surfaces it (single runnable project ⇒ auto-selected, no picker) and dispatches
///      runCommandManaged with the custom executable and parsed arguments.
///   2. workspace/debug evaluates it as runnable — it appears in the project picker. We dismiss the
///      picker so no real debug session is started (the runnable evaluation is all we assert).
///
/// Both commands share <see cref="EasyDotnet.IDE.Workspace.Services.WorkspaceProjectResolver"/>,
/// which filters on <c>IsRunnable</c>; <c>IsRunnable</c> is true for a non-Exe project whenever
/// <c>RunCommand</c> is non-empty.
/// </summary>
public abstract class WorkspaceRunCustomRunCommandTests<TContainer> : WorkspaceRunTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private const string CustomRunCommand = "my-custom-runner";
  private const string CustomRunArguments = "--from-run-arguments alpha beta";

  private Task BeginDebug(bool useDefault = false) =>
    BeginCall(Container.Rpc.WorkspaceDebugAsync(useDefault));

  [Fact]
  public async Task Run_LibraryWithCustomRunCommand_SurfacesAsRunnableAndRunsCustomCommand()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("LibraryWithRunCommand", p => p
        .AsLibrary()
        .WithProperty("RunCommand", CustomRunCommand)
        .WithProperty("RunArguments", CustomRunArguments))
      .Build();
    await InitializeWorkspaceAsync(ws);

    // Single runnable project ⇒ auto-selected, no picker.
    await BeginRun();
    var job = await ReceiveRunCommandAsync();

    Assert.Equal(0, SelectionCallCount);
    Assert.Equal(CustomRunCommand, job.Command.Executable);
    Assert.Equal(new List<string> { "--from-run-arguments", "alpha", "beta" }, job.Command.Arguments);
  }

  [Fact]
  public async Task Debug_LibraryWithCustomRunCommand_EvaluatesProjectAsRunnable()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("LibraryWithRunCommand", p => p
        .AsLibrary()
        .WithProperty("RunCommand", CustomRunCommand)
        .WithProperty("RunArguments", CustomRunArguments))
      .WithProject("ProjectBeta") // ordinary Exe ⇒ also runnable, forces the picker
      .Build();
    await InitializeWorkspaceAsync(ws);

    // Two runnable projects ⇒ picker shown. Dismiss it so no real debug session starts.
    var debugTask = BeginDebug();
    var selection = await ReceiveSelectionAsync(_ => null);
    await debugTask;

    Assert.Contains(selection.Choices, c => c.Display.Contains("LibraryWithRunCommand"));
    Assert.Equal(1, SelectionCallCount);
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called when the picker is dismissed");
  }
}

public sealed class WorkspaceRunCustomRunCommandSdk10Linux : WorkspaceRunCustomRunCommandTests<Sdk10LinuxContainer>;
