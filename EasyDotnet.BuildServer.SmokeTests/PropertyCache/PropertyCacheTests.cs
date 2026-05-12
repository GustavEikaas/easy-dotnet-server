using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyDotnet.BuildServer.Contracts;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.SmokeTests.PropertyCache;

public sealed class PropertyCacheTests
{
  public static IEnumerable<object[]> Targets() => PropertyCacheHarness.Targets();

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Cold_Call_Evaluates_And_Writes_Cache_File(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    var results = await h.EvaluateAsync(csproj);
    Assert.All(results, r => Assert.Null(r.Error));

    var stats = await h.GetStatsAsync();
    Assert.Equal(1, stats.Evaluations);
    Assert.Equal(0, stats.MemoryHits);

    var files = Directory.GetFiles(h.CacheDir);
    Assert.Single(files);
    Assert.EndsWith(".props.json", files[0]);
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Second_Call_Hits_Memory(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    var r1 = await h.EvaluateAsync(csproj);
    var r2 = await h.EvaluateAsync(csproj);

    Assert.All(r1.Concat(r2), r => Assert.Null(r.Error));
    Assert.Equal(r1[0].Project!.AssemblyName, r2[0].Project!.AssemblyName);
    Assert.Equal(r1[0].Project!.TargetPath, r2[0].Project!.TargetPath);

    var stats = await h.GetStatsAsync();
    Assert.Equal(1, stats.Evaluations);
    Assert.True(stats.MemoryHits >= 1, $"expected memory hits >= 1, got {stats.MemoryHits}");
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Touching_Csproj_Invalidates(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    await h.EvaluateAsync(csproj);
    File.SetLastWriteTimeUtc(csproj, DateTime.UtcNow.AddMinutes(1));
    await h.EvaluateAsync(csproj);

    var stats = await h.GetStatsAsync();
    Assert.Equal(2, stats.Evaluations);
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Touching_Directory_Build_Props_Invalidates(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();
    var projDir = Path.GetDirectoryName(csproj)!;
    var dirBuildProps = Path.Combine(projDir, "Directory.Build.props");
    File.WriteAllText(dirBuildProps, "<Project><PropertyGroup><CustomMarker>v1</CustomMarker></PropertyGroup></Project>");

    await h.EvaluateAsync(csproj);
    File.SetLastWriteTimeUtc(dirBuildProps, DateTime.UtcNow.AddMinutes(1));
    await h.EvaluateAsync(csproj);

    var stats = await h.GetStatsAsync();
    Assert.Equal(2, stats.Evaluations);
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Adding_New_Cs_File_Under_Glob_Invalidates(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();
    var projDir = Path.GetDirectoryName(csproj)!;

    await h.EvaluateAsync(csproj);
    File.WriteAllText(Path.Combine(projDir, "Extra.cs"), "namespace App; public class Extra { }");
    await h.EvaluateAsync(csproj);

    var stats = await h.GetStatsAsync();
    Assert.Equal(2, stats.Evaluations);
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Configuration_Produces_Separate_Entry(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    await h.EvaluateAsync(csproj, "Debug");
    await h.EvaluateAsync(csproj, "Release");

    var stats = await h.GetStatsAsync();
    Assert.Equal(2, stats.Evaluations);
    Assert.Equal(2, Directory.GetFiles(h.CacheDir).Length);
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task MultiTfm_Each_Tfm_Independent(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateMultiTfmProject(tfms: "net8.0;net9.0");

    var first = await h.EvaluateAsync(csproj);
    Assert.Equal(2, first.Count);
    var successCount = first.Count(r => r.Success);
    Assert.True(successCount >= 2,
        "expected both TFMs to evaluate successfully; got: " +
        string.Join(", ", first.Select(r => $"{r.TargetFramework}={(r.Success ? "OK" : r.Error?.Message)}")));

    var stats1 = await h.GetStatsAsync();
    Assert.Equal(successCount, stats1.Evaluations);

    await h.EvaluateAsync(csproj);
    var stats2 = await h.GetStatsAsync();
    Assert.Equal(successCount, stats2.Evaluations);
    Assert.True(stats2.MemoryHits >= successCount);
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Parallel_Same_Project_Single_Evaluation(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    var tasks = Enumerable.Range(0, 10).Select(_ => h.EvaluateAsync(csproj)).ToArray();
    var results = await Task.WhenAll(tasks);
    Assert.All(results, r => Assert.All(r, x => Assert.Null(x.Error)));

    var stats = await h.GetStatsAsync();
    Assert.Equal(1, stats.Evaluations);
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Disk_Cache_Survives_Restart(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    await h.EvaluateAsync(csproj);

    await h.RestartServerAsync();

    await h.EvaluateAsync(csproj);
    var stats = await h.GetStatsAsync();
    Assert.Equal(0, stats.Evaluations);
    Assert.True(stats.DiskHits >= 1, $"expected disk hits >= 1, got {stats.DiskHits}");
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Schema_Version_Mismatch_Is_Ignored(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();

    var diag = await h.Server.Rpc.InvokeAsync<BuildServerDiagnosticsResponse>("diagnostics/buildserver");

    var fakeFileName = ComputeDiskFileName(
        Path.GetFullPath(csproj),
        "Debug",
        "net8.0",
        diag.MsBuildVersion);

    File.WriteAllText(Path.Combine(h.CacheDir, fakeFileName), "{\"schemaVersion\":999999}");

    await h.EvaluateAsync(csproj);

    var stats = await h.GetStatsAsync();
    Assert.Equal(1, stats.Evaluations);
  }

  private static string ComputeDiskFileName(string projectFullPath, string configuration, string targetFramework, string msBuildVersion)
  {
    var raw = $"{projectFullPath}|{configuration}|{targetFramework}|{msBuildVersion}";
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
    var sb = new StringBuilder(bytes.Length * 2);
    foreach (var b in bytes) sb.Append(b.ToString("x2"));
    return sb.ToString().Substring(0, 16) + ".props.json";
  }
}
