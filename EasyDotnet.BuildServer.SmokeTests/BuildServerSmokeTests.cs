using System.Runtime.InteropServices;
using EasyDotnet.BuildServer.Contracts;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.SmokeTests;

public sealed class BuildServerSmokeTests
{
  public static IEnumerable<object[]> Targets()
  {
    var baseDir = AppContext.BaseDirectory;

    var net8Dll = Path.Combine(baseDir, "BuildServer", "net8.0", "EasyDotnet.BuildServer.dll");
    if (File.Exists(net8Dll))
    {
      yield return new object[] { "net8.0", "dotnet", new[] { net8Dll } };
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      var net472Exe = Path.Combine(baseDir, "BuildServer", "net472", "EasyDotnet.BuildServer.exe");
      if (File.Exists(net472Exe))
      {
        yield return new object[] { "net472", net472Exe, Array.Empty<string>() };
      }
    }
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Diagnostics_Returns_Payload(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var srv = await BuildServerProcess.StartAsync(exe, leadingArgs, TimeSpan.FromSeconds(30));

    var result = await srv.Rpc.InvokeAsync<BuildServerDiagnosticsResponse>("diagnostics/buildserver");

    Assert.NotNull(result);
    Assert.False(string.IsNullOrWhiteSpace(result.MsBuildVersion));
    Assert.False(string.IsNullOrWhiteSpace(result.MsBuildPath));
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task GetPropertiesBatch_Evaluates_Project(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var srv = await BuildServerProcess.StartAsync(exe, leadingArgs, TimeSpan.FromSeconds(30));

    var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Minimal", "Minimal.csproj");
    Assert.True(File.Exists(fixture), $"fixture not found: {fixture}");

    var request = new GetProjectPropertiesBatchRequest([fixture], "Debug");

    var stream = await srv.Rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<ProjectEvaluationResult>>(
        "project/get-properties-batch",
        request);

    var results = new List<ProjectEvaluationResult>();
    await foreach (var r in stream)
    {
      results.Add(r);
    }

    Assert.NotEmpty(results);
    Assert.All(results, r =>
    {
      Assert.Null(r.Error);
    });
  }
}