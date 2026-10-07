using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using EasyDotnet.ContainerTests.Workspace.Build;

namespace EasyDotnet.ContainerTests.GlobalJson;

/// <summary>
/// <para>
/// Verifies BuildServer runtime/SDK selection on a machine with the stable .NET 10 SDK and a
/// .NET 11 RC SDK installed side by side, with no roll-forward env vars set.
/// </para>
/// <para>
/// Root cause: BuildServer's RollForward=LatestMajor does not roll forward to prerelease runtimes,
/// so it stayed on .NET 10, registered the SDK 10 MSBuild and failed net11.0 projects with
/// NETSDK1045, even though the dotnet CLI picks the RC SDK. A global.json pinning the RC also
/// failed, because the runtime folder name (11.0.0-rc.1.*) could not be parsed for --fx-version.
/// </para>
/// <para>
/// #GustavEikaas/easy-dotnet.nvim#1070
/// </para>
/// </summary>
public sealed class PrereleaseSdkBuildServerTests : WorkspaceBuildTestBase<RcSdkLinuxContainer>
{
  [Fact]
  public async Task BuildServer_WithoutGlobalJson_BuildsPreviewTargetFramework()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha", p => p.WithTargetFramework("net11.0"))
      .Build();
    await InitializeWorkspaceAsync(ws);

    Assert.Equal(11, (await GetDiagnosticsAsync()).RuntimeVersionMajor);
    await AssertBuildSucceedsAsync();
  }

  [Fact]
  public async Task BuildServer_WithoutGlobalJson_BuildsStableTargetFramework()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha", p => p.WithTargetFramework("net10.0"))
      .Build();
    await InitializeWorkspaceAsync(ws);

    await AssertBuildSucceedsAsync();
  }

  [Fact]
  public async Task BuildServer_WithGlobalJsonPinningStable_RunsOnStableRuntime()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha", p => p.WithTargetFramework("net10.0"))
      .WithGlobalJson("10.0.201", "latestFeature")
      .Build();
    await InitializeWorkspaceAsync(ws);

    Assert.Equal(10, (await GetDiagnosticsAsync()).RuntimeVersionMajor);
    await AssertBuildSucceedsAsync();
  }

  [Fact]
  public async Task BuildServer_WithGlobalJsonPinningRc_RunsOnRcRuntime()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha", p => p.WithTargetFramework("net11.0"))
      .WithGlobalJson("11.0.100-rc.1", "latestFeature")
      .Build();
    await InitializeWorkspaceAsync(ws);

    Assert.Equal(11, (await GetDiagnosticsAsync()).RuntimeVersionMajor);
    await AssertBuildSucceedsAsync();
  }

  [Fact]
  public async Task BuildServer_WithGlobalJsonDisallowingPrerelease_BuildsStableTargetFramework()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha", p => p.WithTargetFramework("net10.0"))
      .WithGlobalJsonAllowPrerelease(false)
      .Build();
    await InitializeWorkspaceAsync(ws);

    Assert.Equal(10, (await GetDiagnosticsAsync()).RuntimeVersionMajor);
    await AssertBuildSucceedsAsync();
  }

  private Task<BuildServerDiagnosticsResponse> GetDiagnosticsAsync() =>
    Container.Rpc
      .InvokeAsync<BuildServerDiagnosticsResponse>("diagnostics/buildserver")
      .WaitAsync(TimeSpan.FromMinutes(2));

  private async Task AssertBuildSucceedsAsync()
  {
    await BeginBuild();

    // Surface the MSBuild errors (e.g. NETSDK1045) instead of a bare "no displayMessage" failure.
    if (TryReceiveQuickFixSet(out var errors))
      Assert.Fail($"Build failed:\n{string.Join("\n", errors.Select(i => i.Text))}");

    Assert.Equal("Build succeeded.", await ReceiveDisplayMessageAsync());
  }
}
