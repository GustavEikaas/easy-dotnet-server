using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.BuildServer.SmokeTests.PropertyCache;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.SmokeTests.Futd;

public sealed class FutdTests
{
  public static IEnumerable<object[]> Targets() => PropertyCacheHarness.Targets();

  private static async Task RestoreAsync(BuildServerProcess srv, string projectPath)
  {
    var stream = await srv.Rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<RestoreResult>>(
        "projects/restore",
        new RestoreRequest([projectPath]));
    await foreach (var r in stream)
    {
      Assert.True(r.Success, $"restore failed: {r.ErrorMessage} {(r.Output is null ? "" : string.Join("; ", r.Output.Diagnostics.Select(d => d.Message)))}");
    }
  }

  private static async Task<List<BatchBuildResult>> BuildAsync(BuildServerProcess srv, string projectPath, string targetFramework, string configuration = "Debug")
  {
    var stream = await srv.Rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<BatchBuildResult>>(
        "projects/batchBuild",
        new BatchBuildRequest([projectPath], configuration, targetFramework, "Build"));
    var results = new List<BatchBuildResult>();
    await foreach (var r in stream) { results.Add(r); }
    return results;
  }

  private static BatchBuildResult Finished(IEnumerable<BatchBuildResult> results)
      => results.Single(r => r.Kind == BatchBuildResultKind.Finished);

  private static bool IsFutdHit(BatchBuildResult finished)
      => finished.Success == true
         && finished.Output is not null
         && finished.Output.Diagnostics.Any(d => d.Code == "ED-FUTD");

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Second_Build_Is_Futd(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    await RestoreAsync(h.Server, csproj);

    var first = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(first.Success, $"first build failed: {first.ErrorMessage}");
    Assert.False(IsFutdHit(first), "first build must not be FUTD");

    var second = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(second.Success, $"second build failed: {second.ErrorMessage}");
    Assert.True(IsFutdHit(second), $"second build should be FUTD; got duration={second.Output?.Duration}, diags={string.Join(";", second.Output?.Diagnostics.Select(d => $"{d.Code}:{d.Message}") ?? [])}");
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Touching_Source_Triggers_Real_Build(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();
    var projDir = Path.GetDirectoryName(csproj)!;

    await RestoreAsync(h.Server, csproj);
    var first = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(first.Success);

    File.SetLastWriteTimeUtc(Path.Combine(projDir, "Class1.cs"), DateTime.UtcNow.AddMinutes(1));

    var third = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(third.Success);
    Assert.False(IsFutdHit(third), "build after source touch must not be FUTD");
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task DisableFastUpToDateCheck_Opts_Out(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject(
        extraPropertyGroupXml: "<DisableFastUpToDateCheck>true</DisableFastUpToDateCheck>");

    await RestoreAsync(h.Server, csproj);
    var first = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(first.Success);

    var second = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(second.Success);
    Assert.False(IsFutdHit(second), "FUTD must be disabled by DisableFastUpToDateCheck=true");
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Missing_TargetFramework_Skips_Futd(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    await RestoreAsync(h.Server, csproj);
    var first = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(first.Success);

    var stream = await h.Server.Rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<BatchBuildResult>>(
        "projects/batchBuild",
        new BatchBuildRequest([csproj], "Debug", null, "Build"));
    var results = new List<BatchBuildResult>();
    await foreach (var r in stream) { results.Add(r); }
    var second = Finished(results);

    Assert.True(second.Success);
    Assert.False(IsFutdHit(second), "FUTD must be skipped when TargetFramework is not set");
  }
}
