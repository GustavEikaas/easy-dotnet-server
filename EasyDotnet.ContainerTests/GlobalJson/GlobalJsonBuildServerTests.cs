using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.GlobalJson;

/// <summary>
/// Verifies that the BuildServer is spawned on the .NET 8 runtime when a workspace
/// has a global.json pinning .NET 8 and the container defaults to .NET 10.
///
/// Root cause: BuildHostFactory runs `dotnet exec &lt;BuildServer.dll&gt;` from the tool
/// install directory (no global.json there).  With &lt;RollForward&gt;LatestMajor&lt;/RollForward&gt;
/// in BuildServer.csproj the process lands on .NET 10 regardless of the workspace's
/// global.json — the wrong MSBuild instance is then registered, and NuGet SDK
/// resolution fails.
///
/// The diagnostics/buildserver endpoint directly exposes Environment.Version.Major
/// from inside the BuildServer process, making the mismatch immediately observable.
///
/// WITHOUT FIX: BuildServer runs on .NET 10 → RuntimeVersionMajor == 10 → assertion fails.
/// WITH FIX   : BuildServer is spawned with --fx-version pinned to .NET 8 → RuntimeVersionMajor == 8 → passes.
/// </summary>
public sealed class GlobalJsonBuildServerTests : ContainerTestBase<MultiSdkLinuxContainer>
{
  [Fact]
  public async Task BuildServer_WithGlobalJsonPinningNet8_RunsOnNet8Runtime()
  {
    using var solution = new TempContainerSolution(
      globalJsonSdkVersion: "8.0.0",
      globalJsonRollForward: "latestFeature");

    await InitializeWorkspaceAsync(solution);

    var diagnostics = await Container.Rpc
      .InvokeAsync<BuildServerDiagnosticsResponse>("diagnostics/buildserver")
      .WaitAsync(TimeSpan.FromMinutes(2));

    // The BuildServer must run on the .NET 8 runtime as required by global.json.
    // If it runs on .NET 10 (the container default), this assertion catches the bug.
    Assert.Equal(8, diagnostics.RuntimeVersionMajor);
  }
}