using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Test;

/// <summary>
/// Verifies that the GlobalJson test runner setting controls the dotnet test argument shape:
///   B1. No global.json (or no MTP runner set) → VsTest form:
///       dotnet test &lt;path&gt; --framework &lt;tfm&gt; --no-restore --no-build
///   B2. global.json with test.runner=Microsoft.Testing.Platform → MTP form:
///       dotnet test --project &lt;path&gt; --no-restore --no-build  (no --framework)
///   B3. workspace/test-solution, no MTP runner → positional solution argument:
///       dotnet test &lt;solutionfile&gt; --no-restore --no-build
///   B4. workspace/test-solution, MTP runner global.json → --solution flag:
///       dotnet test --solution &lt;solutionfile&gt; --no-restore --no-build
///   D2. testArgs appended to solution test command.
/// </summary>
public abstract class WorkspaceTestGlobalJsonTests<TContainer> : WorkspaceTestTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Test_WithVsTestProject_NoGlobalJson_UsesFrameworkFlag()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTest();
    await ReceiveSelectionAsync(req => req.Choices.First(c => c.Display.Contains("TestAlpha")).Id);
    var job = await ReceiveRunCommandAsync();
    await testTask;

    Assert.Equal("dotnet", job.Command.Executable);
    var args = job.Command.Arguments;
    Assert.Equal("test", args[0]);
    Assert.DoesNotContain("--project", args);
    Assert.Contains(args, a => a.Contains("TestAlpha.csproj"));
    Assert.Contains("--framework", args);
    Assert.Contains("--no-restore", args);
    Assert.Contains("--no-build", args);
  }

  [Fact]
  public async Task Test_WithMtpRunnerGlobalJson_UsesProjectFlag()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .WithMtpRunnerGlobalJson()
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTest();
    await ReceiveSelectionAsync(req => req.Choices.First(c => c.Display.Contains("TestAlpha")).Id);
    var job = await ReceiveRunCommandAsync();
    await testTask;

    Assert.Equal("dotnet", job.Command.Executable);
    var args = job.Command.Arguments;
    Assert.Equal("test", args[0]);
    Assert.Contains("--project", args);
    var projectFlagIndex = args.IndexOf("--project");
    Assert.True(projectFlagIndex >= 0 && projectFlagIndex + 1 < args.Count,
      "Expected --project flag followed by path");
    Assert.Contains("TestAlpha.csproj", args[projectFlagIndex + 1]);
    Assert.DoesNotContain("--framework", args);
    Assert.Contains("--no-restore", args);
    Assert.Contains("--no-build", args);
  }

  [Fact]
  public async Task Test_Solution_WithoutMtpRunner_UsesPositionalSolutionArg()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTestSolution();
    var job = await ReceiveRunCommandAsync();
    await testTask;

    Assert.Equal("dotnet", job.Command.Executable);
    var args = job.Command.Arguments;
    Assert.Equal("test", args[0]);
    Assert.DoesNotContain("--solution", args);
    Assert.Contains(args, a => a.EndsWith(".slnx"));
    Assert.Contains("--no-restore", args);
    Assert.Contains("--no-build", args);
  }

  [Fact]
  public async Task Test_Solution_WithMtpRunnerGlobalJson_UsesSolutionFlag()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .WithMtpRunnerGlobalJson()
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTestSolution();
    var job = await ReceiveRunCommandAsync();
    await testTask;

    Assert.Equal("dotnet", job.Command.Executable);
    var args = job.Command.Arguments;
    Assert.Equal("test", args[0]);
    Assert.Contains("--solution", args);
    var solutionFlagIndex = args.IndexOf("--solution");
    Assert.True(solutionFlagIndex >= 0 && solutionFlagIndex + 1 < args.Count,
      "Expected --solution flag followed by path");
    Assert.Contains(".slnx", args[solutionFlagIndex + 1]);
    Assert.Contains("--no-restore", args);
    Assert.Contains("--no-build", args);
  }

  [Fact]
  public async Task Test_WithTestArgs_AppendedToSolutionTestCommand()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("TestAlpha", p => p.AsVsTestProject())
      .Build();
    await InitializeWorkspaceAsync(ws);

    var testTask = BeginTestSolution(testArgs: "--filter Category=Integration");
    var job = await ReceiveRunCommandAsync();
    await testTask;

    Assert.Contains("--filter Category=Integration", job.Command.Arguments);
    var noBuildIndex = job.Command.Arguments.IndexOf("--no-build");
    var filterIndex = job.Command.Arguments.IndexOf("--filter Category=Integration");
    Assert.True(filterIndex > noBuildIndex, "testArgs must follow --no-build");
  }
}

public sealed class WorkspaceTestGlobalJsonSdk10Linux : WorkspaceTestGlobalJsonTests<Sdk10LinuxContainer>;