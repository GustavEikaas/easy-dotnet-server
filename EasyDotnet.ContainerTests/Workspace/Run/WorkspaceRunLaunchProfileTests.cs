using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Run;

/// <summary>
/// Verifies launch-profile behaviour in workspace/run:
///   1. Only profiles with commandName "Project" (or omitted) appear in the picker —
///      IIS/IISExpress profiles are filtered out.
///   2. The RunCommand receives the environment variables declared in the chosen profile.
///   3. MSBuild variable references (e.g. $(ProjectDir)) in workingDirectory are
///      interpolated before the RunCommand is sent to the client.
///   4. Both the project and the launch profile are persisted — a second call with
///      useDefault=true bypasses both pickers and applies the same profile.
/// </summary>
public abstract class WorkspaceRunLaunchProfileTests<TContainer> : WorkspaceRunTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  /// <summary>
  /// Picks ProjectAlpha from the project picker by matching on the display label.
  /// Falls back to the first option if no match is found.
  /// </summary>
  private static string PickAlpha(TestPromptSelectionRequest req) =>
    Array.Find(req.Choices, c => c.Display.Contains("ProjectAlpha"))?.Id ?? req.Choices[0].Id;

  [Fact]
  public async Task Run_WithMixedCommandNameProfiles_OnlyShowsProjectTypeAndAppliesEnvVars()
  {
    using var solution = new TempContainerSolution();

    TempContainerSolution.WriteProjectLaunchSettings(solution.Project1Dir, """
      {
        "profiles": {
          "MyProfile": {
            "commandName": "Project",
            "environmentVariables": { "MY_VAR": "hello_from_lp" }
          },
          "IISProfile": {
            "commandName": "IISExpress"
          }
        }
      }
      """);

    await InitializeWorkspaceAsync(solution);

    var runTask = Container.Rpc.WorkspaceRunAsync(useLaunchProfile: true);

    await ReceiveSelectionAsync(PickAlpha);
    var lpPicker = await ReceiveSelectionAsync(req => req.Choices[0].Id);

    Assert.Contains(lpPicker.Choices, c => c.Id == "MyProfile");
    Assert.DoesNotContain(lpPicker.Choices, c => c.Id == "IISProfile");

    await runTask;
    var job = await ReceiveRunCommandAsync();

    Assert.True(
      job.Command.EnvironmentVariables.TryGetValue("MY_VAR", out var myVar),
      "Expected MY_VAR in RunCommand.EnvironmentVariables");
    Assert.Equal("hello_from_lp", myVar);
  }

  [Fact]
  public async Task Run_WithWorkingDirInterpolation_ResolvesMsBuildVariables()
  {
    using var solution = new TempContainerSolution();

    TempContainerSolution.WriteProjectLaunchSettings(solution.Project1Dir, """
      {
        "profiles": {
          "DevProfile": {
            "commandName": "Project",
            "workingDirectory": "$(ProjectDir)run-here",
            "environmentVariables": { "INTERP_VAR": "ok" }
          }
        }
      }
      """);

    await InitializeWorkspaceAsync(solution);

    var runTask = Container.Rpc.WorkspaceRunAsync(useLaunchProfile: true);

    await ReceiveSelectionAsync(PickAlpha);
    await ReceiveSelectionAsync(req => req.Choices[0].Id);

    await runTask;
    var job = await ReceiveRunCommandAsync();

    Assert.DoesNotContain("$(ProjectDir)", job.Command.WorkingDirectory);
    Assert.EndsWith("run-here", job.Command.WorkingDirectory);

    Assert.True(
      job.Command.EnvironmentVariables.TryGetValue("INTERP_VAR", out var interpVar),
      "Expected INTERP_VAR in RunCommand.EnvironmentVariables");
    Assert.Equal("ok", interpVar);
  }

  [Fact]
  public async Task Run_WithLaunchProfile_PersistsProfileAndBypassesBothPickersOnSecondCall()
  {
    using var solution = new TempContainerSolution();

    TempContainerSolution.WriteProjectLaunchSettings(solution.Project1Dir, """
      {
        "profiles": {
          "MyProfile": {
            "commandName": "Project",
            "environmentVariables": { "MY_VAR": "hello_from_lp" }
          }
        }
      }
      """);

    await InitializeWorkspaceAsync(solution);

    var runTask1 = Container.Rpc.WorkspaceRunAsync(useLaunchProfile: true);

    await ReceiveSelectionAsync(PickAlpha);
    await ReceiveSelectionAsync(req => req.Choices[0].Id);
    await runTask1;
    var job1 = await ReceiveRunCommandAsync();

    Assert.Equal(2, SelectionCallCount);

    await Container.Rpc.WorkspaceRunAsync(useDefault: true, useLaunchProfile: true);

    var job2 = await ReceiveRunCommandAsync();

    Assert.Equal(2, SelectionCallCount);

    Assert.True(job1.Command.EnvironmentVariables.TryGetValue("MY_VAR", out var v1));
    Assert.True(job2.Command.EnvironmentVariables.TryGetValue("MY_VAR", out var v2));
    Assert.Equal(v1, v2);
  }

  /// <summary>
  /// Verifies argument precedence when both a launch profile and cliArgs supply arguments:
  ///   [targetPath, ...lpCommandLineArgs, "--", ...cliArgs]
  ///
  /// LP args come first (no separator) so the binary receives them as normal flags.
  /// cliArgs follow after "--" so they are passed through verbatim.
  /// This matches the behaviour expected by ASP.NET Core and most .NET CLI apps.
  /// </summary>
  [Fact]
  public async Task Run_WithLaunchProfileArgsAndCliArgs_LpArgsPrecedeCliArgsSeparatedByDashDash()
  {
    using var solution = new TempContainerSolution();

    TempContainerSolution.WriteProjectLaunchSettings(solution.Project1Dir, """
      {
        "profiles": {
          "ArgsProfile": {
            "commandName": "Project",
            "commandLineArgs": "--lp-flag lp-value"
          }
        }
      }
      """);

    await InitializeWorkspaceAsync(solution);

    var runTask = Container.Rpc.WorkspaceRunAsync(useLaunchProfile: true, cliArgs: "user-arg");

    await ReceiveSelectionAsync(PickAlpha);
    await ReceiveSelectionAsync(req => req.Choices[0].Id);
    await runTask;
    var job = await ReceiveRunCommandAsync();

    var args = job.Command.Arguments;
    var lpFlagIndex = args.IndexOf("--lp-flag");
    var separatorIndex = args.IndexOf("--");
    var userArgIndex = args.IndexOf("user-arg");

    Assert.True(lpFlagIndex >= 0, "Expected --lp-flag from launch profile commandLineArgs");
    Assert.True(separatorIndex >= 0, "Expected -- separator before cliArgs");
    Assert.True(userArgIndex >= 0, "Expected user-arg from cliArgs");

    // LP args must come before the -- separator, cliArgs must come after.
    Assert.True(lpFlagIndex < separatorIndex, "LP args must precede the -- separator");
    Assert.True(separatorIndex < userArgIndex, "cliArgs must follow the -- separator");
    Assert.Equal(lpFlagIndex + 1, args.IndexOf("lp-value")); // lp-value immediately follows --lp-flag
  }

  [Fact]
  public async Task Run_WithPersistedLaunchProfileRemovedFromLaunchSettings_ClearsProfileAndShowsPickerAgain()
  {
    using var solution = new TempContainerSolution();

    TempContainerSolution.WriteProjectLaunchSettings(solution.Project1Dir, """
      {
        "profiles": {
          "MyProfile": {
            "commandName": "Project",
            "environmentVariables": { "MY_VAR": "hello_from_lp" }
          }
        }
      }
      """);

    await InitializeWorkspaceAsync(solution);

    // First run: pick ProjectAlpha and MyProfile, both persisted.
    var runTask1 = Container.Rpc.WorkspaceRunAsync(useLaunchProfile: true);
    await ReceiveSelectionAsync(PickAlpha);
    await ReceiveSelectionAsync(req => req.Choices[0].Id);
    await runTask1;
    await ReceiveRunCommandAsync();

    Assert.Equal(2, SelectionCallCount);

    // Remove MyProfile from launchSettings without removing the file itself.
    TempContainerSolution.WriteProjectLaunchSettings(solution.Project1Dir, """
      {
        "profiles": {
          "OtherProfile": {
            "commandName": "Project"
          }
        }
      }
      """);

    // Second run: project is still in solution so project picker is bypassed,
    // but the persisted LP no longer exists — LP picker must be shown again.
    var runTask2 = Container.Rpc.WorkspaceRunAsync(useDefault: true, useLaunchProfile: true);
    await ReceiveSelectionAsync(req => req.Choices[0].Id);
    await runTask2;
    await ReceiveRunCommandAsync();

    Assert.Equal(3, SelectionCallCount);
  }
}

public sealed class WorkspaceRunLaunchProfileSdk8Linux : WorkspaceRunLaunchProfileTests<Sdk8LinuxContainer>;
public sealed class WorkspaceRunLaunchProfileSdk9Linux : WorkspaceRunLaunchProfileTests<Sdk9LinuxContainer>;
public sealed class WorkspaceRunLaunchProfileSdk10Linux : WorkspaceRunLaunchProfileTests<Sdk10LinuxContainer>;