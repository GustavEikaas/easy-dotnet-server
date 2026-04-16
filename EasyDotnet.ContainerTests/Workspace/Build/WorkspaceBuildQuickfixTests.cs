using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.Workspace.Build;

/// <summary>
/// Verifies quickfix-mode build behavior for workspace/build:
///   1. Clean build => success message, no quickfix notifications.
///   2. Warning-only build => quickfix/set-silent + success-with-warning-count message.
///   3. Error build => quickfix/set + failure summary displayError.
/// </summary>
public abstract class WorkspaceBuildQuickfixTests<TContainer> : WorkspaceBuildTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task Build_QuickfixMode_WhenBuildSucceeds_DisplaysSuccessWithoutQuickFix()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var buildTask = BeginBuild();
    var message = await ReceiveDisplayMessageAsync();
    await buildTask;

    Assert.Equal("Build succeeded.", message);
    Assert.True(QuickFixSetNotReceived(), "quickfix/set must not be sent for clean builds");
    Assert.True(QuickFixSetSilentNotReceived(), "quickfix/set-silent must not be sent for clean builds");
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called in quickfix mode");
  }

  [Fact]
  public async Task Build_QuickfixMode_WhenBuildHasWarnings_UsesSilentQuickFixAndSuccessMessage()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha")
      .Build();
    ws.Project("AppAlpha").WriteBuildWarningFixture();
    await InitializeWorkspaceAsync(ws);

    var buildTask = BeginBuild();
    var warnings = await ReceiveQuickFixSetSilentAsync();
    var message = await ReceiveDisplayMessageAsync();
    await buildTask;

    Assert.NotEmpty(warnings);
    Assert.All(warnings, item => Assert.Equal(TestQuickFixItemType.Warning, item.Type));
    Assert.StartsWith("Build succeeded", message);
    Assert.Contains("warning(s)", message);

    Assert.True(QuickFixSetNotReceived(), "quickfix/set must not be sent for warning-only builds");
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called in quickfix mode");
  }

  [Fact]
  public async Task Build_QuickfixMode_WhenBuildFails_SetsQuickFixAndDisplaysFailureSummary()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha")
      .Build();
    ws.Project("AppAlpha").WriteBuildErrorFixture();
    await InitializeWorkspaceAsync(ws);

    var buildTask = BeginBuild();
    var items = await ReceiveQuickFixSetAsync();
    var error = await ReceiveDisplayErrorAsync();
    await buildTask;

    Assert.NotEmpty(items);
    Assert.Contains(items, item => item.Type == TestQuickFixItemType.Error);
    Assert.StartsWith("Build FAILED", error);

    Assert.True(QuickFixSetSilentNotReceived(), "quickfix/set-silent must not be sent for failed builds");
    Assert.True(RunCommandNotReceived(), "runCommandManaged must not be called in quickfix mode");
  }
}

public sealed class WorkspaceBuildQuickfixSdk10Linux : WorkspaceBuildQuickfixTests<Sdk10LinuxContainer>;
