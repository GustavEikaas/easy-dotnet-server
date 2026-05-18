using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.GlobalJson;

/// <summary>
/// <para>
/// Verifies that BuildHostFactory honors global.json's <c>version</c> + <c>rollForward</c>
/// semantics the way the dotnet CLI does (see
/// runtime/src/native/corehost/fxr/sdk_resolver.cpp). The BuildServer must land on a
/// runtime matching the SDK selected by global.json, or fail to start if no SDK satisfies
/// the policy.
/// </para>
/// <para>
/// The container provides both .NET 8 and .NET 10 SDKs and runtimes. Each test pins a
/// different version/rollForward combination and asserts via the diagnostics/buildserver
/// RPC which runtime the BuildServer actually landed on.
/// </para>
/// <para>
/// #GustavEikaas/easy-dotnet.nvim#901
/// #GustavEikaas/easy-dotnet-server#357
/// </para>
/// </summary>
public sealed class GlobalJsonBuildServerTests : ContainerTestBase<MultiSdkLinuxContainer>
{
  [Fact]
  public async Task BuildServer_WithGlobalJsonPinningNet8_RunsOnNet8Runtime()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithProject("ProjectBeta")
      .WithGlobalJson("8.0.0", "latestFeature")
      .Build();

    await InitializeWorkspaceAsync(ws);

    var diagnostics = await Container.Rpc
      .InvokeAsync<BuildServerDiagnosticsResponse>("diagnostics/buildserver")
      .WaitAsync(TimeSpan.FromMinutes(2));

    Assert.Equal(8, diagnostics.RuntimeVersionMajor);

    // The BuildServer should be on the highest installed 8.x Microsoft.NETCore.App
    // patch in the container — the same runtime the dotnet host would pick when
    // exec'ing on the .NET 8 line.
    var expected = await GetHighestInstalledRuntimeAsync(majorVersion: 8);
    Assert.Equal(expected, diagnostics.RuntimeVersion);
  }

  /// <summary>
  /// global.json with <c>rollForward: disable</c> requires an exact SDK match.
  /// The requested patch is not installed in the container, so BuildServer
  /// startup must fail (mirroring <c>dotnet</c>'s strict failure).
  /// </summary>
  [Fact]
  public async Task BuildServer_WithRollForwardDisableAndUninstalledVersion_FailsToStart()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithGlobalJson("8.0.999", "disable")
      .Build();

    await InitializeWorkspaceAsync(ws);

    var ex = await Assert.ThrowsAnyAsync<RemoteInvocationException>(async () =>
      await Container.Rpc
        .InvokeAsync<BuildServerDiagnosticsResponse>("diagnostics/buildserver")
        .WaitAsync(TimeSpan.FromMinutes(2)));

    Assert.Contains("compatible .NET SDK was not found", ex.Message);
  }

  /// <summary>
  /// global.json with <c>rollForward: latestMajor</c> permits any installed major.
  /// The container's highest SDK is .NET 10, so the BuildServer should run on .NET 10
  /// — proving rollForward semantics are being applied, not just hardcoded to the
  /// requested major.
  /// </summary>
  [Fact]
  public async Task BuildServer_WithRollForwardLatestMajor_RunsOnHighestInstalledMajor()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("ProjectAlpha")
      .WithGlobalJson("8.0.0", "latestMajor")
      .Build();

    await InitializeWorkspaceAsync(ws);

    var diagnostics = await Container.Rpc
      .InvokeAsync<BuildServerDiagnosticsResponse>("diagnostics/buildserver")
      .WaitAsync(TimeSpan.FromMinutes(2));

    Assert.Equal(10, diagnostics.RuntimeVersionMajor);
  }

  /// <summary>
  /// Parses <c>dotnet --list-runtimes</c> output from the container and returns
  /// the highest installed <c>Microsoft.NETCore.App</c> version with the given major.
  /// </summary>
  private async Task<string> GetHighestInstalledRuntimeAsync(int majorVersion)
  {
    var (stdout, _, exit) = await Container.ExecAsync(["dotnet", "--list-runtimes"]);
    Assert.Equal(0, exit);

    // Format: "Microsoft.NETCore.App 8.0.27 [/usr/share/dotnet/shared/Microsoft.NETCore.App]"
    var versions = stdout
      .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .Where(l => l.StartsWith("Microsoft.NETCore.App "))
      .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])
      .Select(v => Version.TryParse(v, out var parsed) ? parsed : null)
      .OfType<Version>()
      .Where(v => v.Major == majorVersion)
      .ToList();

    Assert.NotEmpty(versions);
    return versions.Max()!.ToString();
  }
}
