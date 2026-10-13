using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using EasyDotnet.ContainerTests.Workspace.Build;

namespace EasyDotnet.ContainerTests.GlobalJson;

/// <summary>
/// <para>
/// Verifies that a global.json pinning an SDK older than .NET 8 does not prevent the BuildServer
/// from starting, on a machine with both the .NET 6 and .NET 10 SDKs installed.
/// </para>
/// <para>
/// Root cause: the BuildServer targets net8.0, but BuildHostFactory pinned it to the .NET 6
/// runtime via --fx-version, so it crashed on startup loading System.Runtime 8.0 and every
/// restore/evaluation timed out. Pins below .NET 8 are now ignored and the BuildServer rolls
/// forward to the newest runtime/SDK.
/// </para>
/// <para>
/// #GustavEikaas/easy-dotnet.nvim#1073
/// </para>
/// </summary>
public sealed class UnsupportedSdkBuildServerTests : WorkspaceBuildTestBase<Net6SdkLinuxContainer>
{
  [Fact]
  public async Task BuildServer_WithGlobalJsonPinningNet6_RollsForwardAndBuilds()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithProject("AppAlpha", p => p.WithTargetFramework("net6.0"))
      .WithGlobalJson("6.0.400", "latestFeature")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var diagnostics = await Container.Rpc
      .InvokeAsync<BuildServerDiagnosticsResponse>("diagnostics/buildserver")
      .WaitAsync(TimeSpan.FromMinutes(2));
    Assert.Equal(10, diagnostics.RuntimeVersionMajor);

    await BeginBuild();

    if (TryReceiveQuickFixSet(out var errors))
      Assert.Fail($"Build failed:\n{string.Join("\n", errors.Select(i => i.Text))}");

    // net6.0 is out of support, so SDK 10 emits NETSDK1138; warnings are fine, errors are not.
    Assert.StartsWith("Build succeeded", await ReceiveDisplayMessageAsync());
  }
}