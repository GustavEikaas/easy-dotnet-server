using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.BuildServer.SmokeTests.PropertyCache;

namespace EasyDotnet.BuildServer.SmokeTests.Build;

public sealed class BatchBuildTests
{
  public static IEnumerable<object[]> Targets() => PropertyCacheHarness.Targets();

  private static async Task RestoreAsync(BuildServerProcess srv, string projectPath)
  {
    var stream = await srv.Rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<RestoreResult>>(
        "projects/restore",
        new RestoreRequest([projectPath]));
    await foreach (var r in stream)
    {
      Assert.True(r.Success, $"restore failed: {r.ErrorMessage}");
    }
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Build_Does_Not_Leak_TargetFramework_To_References_With_SkipGetTargetFrameworkProperties(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);

    var libDir = Path.Combine(h.WorkspaceDir, "Lib");
    Directory.CreateDirectory(libDir);
    var libPath = Path.Combine(libDir, "Lib.csproj");
    File.WriteAllText(libPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
</Project>
""");
    File.WriteAllText(Path.Combine(libDir, "Class1.cs"), "namespace Lib { public class Class1 { } }");

    var appDir = Path.Combine(h.WorkspaceDir, "App");
    Directory.CreateDirectory(appDir);
    var appPath = Path.Combine(appDir, "App.csproj");
    File.WriteAllText(appPath, $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>{tfm}</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{libPath}" SkipGetTargetFrameworkProperties="true" />
  </ItemGroup>
</Project>
""");
    File.WriteAllText(Path.Combine(appDir, "Class2.cs"), "namespace App; public class Class2 { }");

    await RestoreAsync(h.Server, appPath);

    var buildRequest = new BatchBuildRequest(
        ProjectPaths: [appPath],
        Configuration: "Debug",
        TargetFramework: tfm);

    var stream = await h.Server.Rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<BatchBuildResult>>("projects/batchBuild", buildRequest);
    var results = new List<BatchBuildResult>();
    await foreach (var r in stream)
    {
      results.Add(r);
    }

    var finished = results.Single(r => r.Kind == BatchBuildResultKind.Finished);
    var diagnostics = string.Join("; ", finished.Output?.Diagnostics.Select(d => d.Message) ?? []);
    Assert.True(finished.Success, $"build failed: {finished.ErrorMessage} {diagnostics}");
  }
}
