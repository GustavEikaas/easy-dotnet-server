using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Workspace;

/// <summary>
/// Verifies launch-profile behaviour in workspace/run:
///   1. Only profiles with commandName "Project" (or omitted) appear in the picker —
///      IIS/IISExpress profiles are excluded. This test is intentionally written before
///      the filtering fix and will fail until ResolveProfileAsync is updated.
///   2. The RunCommand receives the environment variables declared in the chosen profile.
///   3. MSBuild variable references (e.g. $(ProjectDir)) in workingDirectory are
///      interpolated before the RunCommand is sent to the client.
/// </summary>
public abstract class WorkspaceRunLaunchProfileContainerTests<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private readonly TaskCompletionSource<TestPromptSelectionRequest> _lpPickerTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private readonly TaskCompletionSource<TestTrackedJob> _runJobTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private readonly TaskCompletionSource<TestTrackedJob> _interpolationJobTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private readonly TaskCompletionSource<TestPromptSelectionRequest> _lpPickerInterpolationTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

  protected override void ConfigureRpc(JsonRpc rpc) =>
    rpc.AddLocalRpcTarget(new TestClientHandlers(this), new JsonRpcTargetOptions { DisposeOnDisconnect = false });

  /// <summary>
  /// Verifies that:
  ///   - The launch profile picker only shows profiles whose commandName is "Project" (or absent).
  ///   - The resulting RunCommand.EnvironmentVariables contains the env vars from that profile.
  /// </summary>
  [Fact]
  public async Task Run_WithMixedCommandNameProfiles_OnlyShowsProjectTypeAndAppliesEnvVars()
  {
    using var solution = new TempContainerSolution();

    TempContainerSolution.WriteProjectLaunchSettings(solution.Project1Dir, """
      {
        "profiles": {
          "MyProfile": {
            "commandName": "Project",
            "environmentVariables": {
              "MY_VAR": "hello_from_lp"
            }
          },
          "IISProfile": {
            "commandName": "IISExpress"
          }
        }
      }
      """);

    await InitializeWorkspaceAsync(solution);

    await Container.Rpc.InvokeWithParameterObjectAsync(
      "workspace/run",
      new { useDefault = false, useLaunchProfile = true, filePath = (string?)null, cliArgs = (string?)null });

    var lpPicker = await _lpPickerTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));

    // Only "Project" type should be offered — IISProfile must be absent.
    Assert.Contains(lpPicker.Choices, c => c.Id == "MyProfile");
    Assert.DoesNotContain(lpPicker.Choices, c => c.Id == "IISProfile");

    var job = await _runJobTcs.Task.WaitAsync(TimeSpan.FromMinutes(3));

    Assert.True(
      job.Command.EnvironmentVariables.TryGetValue("MY_VAR", out var myVar),
      "Expected MY_VAR to be present in RunCommand.EnvironmentVariables");
    Assert.Equal("hello_from_lp", myVar);
  }

  /// <summary>
  /// Verifies that MSBuild variable references in a launch profile's workingDirectory
  /// (e.g. $(ProjectDir)) are interpolated before the RunCommand is sent to the client.
  /// </summary>
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
            "environmentVariables": {
              "INTERP_VAR": "ok"
            }
          }
        }
      }
      """);

    await InitializeWorkspaceAsync(solution);

    await Container.Rpc.InvokeWithParameterObjectAsync(
      "workspace/run",
      new { useDefault = false, useLaunchProfile = true, filePath = (string?)null, cliArgs = (string?)null });

    // The launch profile picker for this test goes to a separate TCS.
    var lpPicker = await _lpPickerInterpolationTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
    Assert.Contains(lpPicker.Choices, c => c.Id == "DevProfile");

    var job = await _interpolationJobTcs.Task.WaitAsync(TimeSpan.FromMinutes(3));

    // $(ProjectDir) must have been substituted — the raw token must not survive.
    Assert.DoesNotContain("$(ProjectDir)", job.Command.WorkingDirectory);
    // The suffix we appended must be present.
    Assert.EndsWith("run-here", job.Command.WorkingDirectory);

    Assert.True(
      job.Command.EnvironmentVariables.TryGetValue("INTERP_VAR", out var interpVar),
      "Expected INTERP_VAR to be present in RunCommand.EnvironmentVariables");
    Assert.Equal("ok", interpVar);
  }

  private sealed class TestClientHandlers(WorkspaceRunLaunchProfileContainerTests<TContainer> test)
  {
    private int _selectionCallCount;
    private int _runCommandCallCount;

    /// <summary>
    /// Handles both the project picker and the launch profile picker.
    /// The two are distinguished by the prompt text:
    ///   - "Pick project to run"   → project picker  → always choose the first "ProjectAlpha" entry
    ///   - "Pick launch profile"   → LP picker        → forward to the correct TCS and choose the first option
    /// </summary>
    [JsonRpcMethod("promptSelection", UseSingleObjectParameterDeserialization = true)]
    public Task<string?> PromptSelection(TestPromptSelectionRequest request)
    {
      if (request.Prompt.Contains("launch profile"))
      {
        var callNum = Interlocked.Increment(ref _selectionCallCount);
        if (callNum == 1)
          test._lpPickerTcs.TrySetResult(request);
        else
          test._lpPickerInterpolationTcs.TrySetResult(request);
        // Always accept the first available profile.
        return Task.FromResult<string?>(request.Choices[0].Id);
      }

      // Project picker — choose ProjectAlpha (the project that has launchSettings.json).
      var alpha = Array.Find(request.Choices, c => c.Display.Contains("ProjectAlpha"));
      return Task.FromResult<string?>(alpha?.Id ?? request.Choices[0].Id);
    }

    /// <summary>
    /// Capture the RunCommand then reject — same pattern as WorkspaceRunContainerTests.
    /// Rejecting triggers SetFailedToStart which cleanly releases the LongRunning slot.
    /// </summary>
    [JsonRpcMethod("runCommandManaged", UseSingleObjectParameterDeserialization = true)]
    public Task<object> RunCommandManaged(TestTrackedJob job)
    {
      var callNum = Interlocked.Increment(ref _runCommandCallCount);
      if (callNum == 1) test._runJobTcs.TrySetResult(job);
      else test._interpolationJobTcs.TrySetResult(job);

      return Task.FromException<object>(new InvalidOperationException("Test cancelled run — no process spawning in container tests"));
    }
  }
}

public sealed class WorkspaceRunLaunchProfileSdk8Linux : WorkspaceRunLaunchProfileContainerTests<Sdk8LinuxContainer>;
public sealed class WorkspaceRunLaunchProfileSdk9Linux : WorkspaceRunLaunchProfileContainerTests<Sdk9LinuxContainer>;
public sealed class WorkspaceRunLaunchProfileSdk10Linux : WorkspaceRunLaunchProfileContainerTests<Sdk10LinuxContainer>;
