using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Test;

/// <summary>
/// Verifies workspace/test project-picker behaviour when a solution file is present:
///   A1. A promptSelection is shown when no default is set; the picked project is tested with
///       the correct VsTest argument shape: dotnet test &lt;path&gt; --framework &lt;tfm&gt; --no-restore --no-build.
///   A2. The picked project is persisted — a second call with useDefault=true bypasses the picker
///       and reproduces an identical command.
///   A3. A persisted project removed from the solution is treated as stale — the picker is re-shown
///       on the next call.
///   A4. Dismissing the picker (returning null) completes workspace/test cleanly without dispatching
///       runCommandManaged or displaying an error.
///   A5. A solution with no test projects displays an error and never dispatches runCommandManaged.
///   A6. Selecting the "Solution" option from the picker runs dotnet test against the solution file.
///   D1. testArgs are appended at the end of the dotnet test command.
/// </summary>
public abstract class WorkspaceTestProjectTests<TContainer> : WorkspaceTestTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Test_WithTwoTestProjects_ShowsPickerAndRunsCorrectCommand()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .WithProject("TestBeta", p => p.AsVsTestProject())
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTest();
    await ReceiveSelectionAsync(req => req.Choices.First(c => c.Display.Contains("TestAlpha")).Id);
    var job = await ReceiveRunCommandAsync();
    await testTask;

    Assert.Equal("dotnet", job.Command.Executable);
    Assert.Equal("test", job.Command.Arguments[0]);
    // VsTest form: dotnet test <path> --framework <tfm>
    Assert.Contains(job.Command.Arguments, a => a.Contains("TestAlpha.csproj"));
    Assert.Contains("--framework", job.Command.Arguments);
    Assert.Contains("--no-restore", job.Command.Arguments);
    Assert.Contains("--no-build", job.Command.Arguments);
    // MTP --project flag must NOT appear for VsTest
    Assert.DoesNotContain("--project", job.Command.Arguments);
    Assert.Equal(1, SelectionCallCount);
  }

  [Fact]
  public async Task Test_PersistsDefaultAndBypassesPickerOnSecondCall()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .WithProject("TestBeta", p => p.AsVsTestProject())
      .Build();
    await InitializeWorkspaceAsync(ws);

    // First call: picker shown, pick TestAlpha.
    var testTask1 = BeginTest();
    await ReceiveSelectionAsync(req => req.Choices.First(c => c.Display.Contains("TestAlpha")).Id);
    var job1 = await ReceiveRunCommandAsync();
    await testTask1;
    Assert.Equal(1, SelectionCallCount);

    // Second call with useDefault=true: picker bypassed.
    var testTask2 = BeginTest(useDefault: true);
    var job2 = await ReceiveRunCommandAsync();
    await testTask2;

    Assert.Equal(1, SelectionCallCount);
    Assert.Equal(job1.Command.Arguments, job2.Command.Arguments);
  }

  [Fact]
  public async Task Test_UserDismissesPicker_CompletesCleanlyWithoutRunning()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .WithProject("TestBeta", p => p.AsVsTestProject())
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTest();
    await ReceiveSelectionAsync(_ => null);
    await testTask;

    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called when picker is dismissed");
    Assert.Equal(1, SelectionCallCount);
  }

  [Fact]
  public async Task Test_SolutionWithNoTestProjects_DisplaysError()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("AppAlpha")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTest();
    var error = await ReceiveDisplayErrorAsync();
    await testTask;

    Assert.Equal("No test projects found in solution", error);
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called when no test projects exist");
    Assert.Equal(0, SelectionCallCount);
  }

  [Fact]
  public async Task Test_SolutionOptionInPicker_RunsTestAgainstEntireSolutionFile()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .WithProject("TestBeta", p => p.AsVsTestProject())
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTest();
    // The "Solution" option has id "__solution__"
    await ReceiveSelectionAsync(req => req.Choices.First(c => c.Display == "Solution").Id);
    var job = await ReceiveRunCommandAsync();
    await testTask;

    Assert.Equal("dotnet", job.Command.Executable);
    Assert.Equal("test", job.Command.Arguments[0]);
    Assert.Contains(job.Command.Arguments, a => a.EndsWith(".slnx"));
    Assert.DoesNotContain("--framework", job.Command.Arguments);
    Assert.Contains("--no-restore", job.Command.Arguments);
    Assert.Contains("--no-build", job.Command.Arguments);
  }

  [Fact]
  public async Task Test_WithTestArgs_AppendedToProjectTestCommand()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTest(testArgs: "--filter Category=Unit");
    await ReceiveSelectionAsync(req => req.Choices.First(c => c.Id != "__solution__").Id);
    var job = await ReceiveRunCommandAsync();
    await testTask;

    Assert.Contains("--filter Category=Unit", job.Command.Arguments);
    // testArgs must come after the standard flags
    var noRestoreIndex = job.Command.Arguments.IndexOf("--no-restore");
    var noBuildIndex = job.Command.Arguments.IndexOf("--no-build");
    var filterIndex = job.Command.Arguments.IndexOf("--filter Category=Unit");
    Assert.True(filterIndex > noRestoreIndex, "testArgs must follow --no-restore");
    Assert.True(filterIndex > noBuildIndex, "testArgs must follow --no-build");
  }
}

public sealed class WorkspaceTestProjectSdk10Linux : WorkspaceTestProjectTests<Sdk10LinuxContainer>;