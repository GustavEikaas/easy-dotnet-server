using System.Runtime.InteropServices;
using EasyDotnet.BuildServer.Contracts;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.SmokeTests.PropertyCache;

internal sealed class PropertyCacheHarness : IAsyncDisposable
{
  public string CacheDir { get; }
  public string WorkspaceDir { get; }
  private readonly string _exe;
  private readonly string[] _leadingArgs;
  public BuildServerProcess Server { get; private set; }

  private PropertyCacheHarness(string cacheDir, string workspaceDir, string exe, string[] leadingArgs, BuildServerProcess server)
  {
    CacheDir = cacheDir;
    WorkspaceDir = workspaceDir;
    _exe = exe;
    _leadingArgs = leadingArgs;
    Server = server;
  }

  public static async Task<PropertyCacheHarness> StartAsync(string exe, string[] leadingArgs)
  {
    var cacheDir = Path.Combine(Path.GetTempPath(), "edcache-" + Guid.NewGuid().ToString("N"));
    var workspaceDir = Path.Combine(Path.GetTempPath(), "edws-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(cacheDir);
    Directory.CreateDirectory(workspaceDir);

    var env = new Dictionary<string, string?>
    {
      ["EASYDOTNET_PROPERTY_CACHE_DIR"] = cacheDir,
    };
    var srv = await BuildServerProcess.StartAsync(exe, leadingArgs, TimeSpan.FromSeconds(30), env);
    return new PropertyCacheHarness(cacheDir, workspaceDir, exe, leadingArgs, srv);
  }

  public async Task RestartServerAsync()
  {
    await Server.DisposeAsync();
    var env = new Dictionary<string, string?>
    {
      ["EASYDOTNET_PROPERTY_CACHE_DIR"] = CacheDir,
    };
    Server = await BuildServerProcess.StartAsync(_exe, _leadingArgs, TimeSpan.FromSeconds(30), env);
  }

  public async Task<PropertyCacheDiagnosticsResponse> GetStatsAsync()
      => await Server.Rpc.InvokeAsync<PropertyCacheDiagnosticsResponse>("diagnostics/property-cache");

  public async Task<List<ProjectEvaluationResult>> EvaluateAsync(string projectPath, string configuration = "Debug")
  {
    var request = new GetProjectPropertiesBatchRequest([projectPath], configuration);
    var stream = await Server.Rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<ProjectEvaluationResult>>(
        "project/get-properties-batch",
        request);
    var results = new List<ProjectEvaluationResult>();
    await foreach (var r in stream)
    {
      results.Add(r);
    }
    return results;
  }

  public string CreateSingleTfmProject(string name = "App", string tfm = "net8.0", string extraPropertyGroupXml = "")
  {
    var dir = Path.Combine(WorkspaceDir, name);
    Directory.CreateDirectory(dir);
    var csproj = Path.Combine(dir, name + ".csproj");
    File.WriteAllText(csproj, $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>{tfm}</TargetFramework>
    <OutputType>Library</OutputType>
    {extraPropertyGroupXml}
  </PropertyGroup>
</Project>
""");
    File.WriteAllText(Path.Combine(dir, "Class1.cs"), "namespace App; public class Class1 { }");
    return csproj;
  }

  public string CreateMultiTfmProject(string name = "Multi", string tfms = "net8.0;net9.0")
  {
    var dir = Path.Combine(WorkspaceDir, name);
    Directory.CreateDirectory(dir);
    var csproj = Path.Combine(dir, name + ".csproj");
    File.WriteAllText(csproj, $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>{tfms}</TargetFrameworks>
    <OutputType>Library</OutputType>
  </PropertyGroup>
</Project>
""");
    File.WriteAllText(Path.Combine(dir, "Class1.cs"), "namespace App; public class Class1 { }");
    return csproj;
  }

  public async ValueTask DisposeAsync()
  {
    try { await Server.DisposeAsync(); } catch { }
    try { Directory.Delete(CacheDir, recursive: true); } catch { }
    try { Directory.Delete(WorkspaceDir, recursive: true); } catch { }
  }

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
}