using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.BuildServer.SmokeTests.PropertyCache;

namespace EasyDotnet.BuildServer.SmokeTests;

public sealed class RestoreNoOpTests
{
  public static IEnumerable<object[]> Targets() => PropertyCacheHarness.Targets();

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task First_Restore_Is_Not_NoOp(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    var result = await RestoreSingleAsync(h.Server, csproj);

    Assert.True(result.Success, result.ErrorMessage);
    Assert.False(result.Output?.NoOp == true);
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Second_Unchanged_Restore_Is_NoOp(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    var first = await RestoreSingleAsync(h.Server, csproj);
    Assert.True(first.Success, first.ErrorMessage);

    var second = await RestoreSingleAsync(h.Server, csproj);

    Assert.True(second.Success, second.ErrorMessage);
    Assert.True(second.Output?.NoOp == true, string.Join("; ", second.Output?.Diagnostics.Select(d => d.Message) ?? []));
    Assert.Contains(second.Output?.Diagnostics ?? [], d => d.Code == "ED-RESTORE-NOOP");
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Missing_Assets_File_Forces_NonNoOp_Restore(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    var first = await RestoreSingleAsync(h.Server, csproj);
    Assert.True(first.Success, first.ErrorMessage);

    var assetsFile = Path.Combine(Path.GetDirectoryName(csproj)!, "obj", "project.assets.json");
    Assert.True(File.Exists(assetsFile), $"expected restore to create {assetsFile}");
    File.Delete(assetsFile);

    var second = await RestoreSingleAsync(h.Server, csproj);

    Assert.True(second.Success, second.ErrorMessage);
    Assert.False(second.Output?.NoOp == true);
  }

  private static async Task<RestoreResult> RestoreSingleAsync(BuildServerProcess srv, string projectPath)
  {
    var stream = await srv.Rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<RestoreResult>>(
        "projects/restore",
        new RestoreRequest([projectPath]));

    var results = new List<RestoreResult>();
    await foreach (var result in stream)
    {
      results.Add(result);
    }

    return Assert.Single(results);
  }
}