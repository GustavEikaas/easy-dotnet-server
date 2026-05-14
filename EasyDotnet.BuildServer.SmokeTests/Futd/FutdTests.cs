using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.BuildServer.SmokeTests.PropertyCache;

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
        new BatchBuildRequest(
            ProjectPaths: [projectPath],
            Configuration: configuration,
            TargetFramework: targetFramework,
            BuildTarget: "Build"));
    var results = new List<BatchBuildResult>();
    await foreach (var r in stream) { results.Add(r); }
    return results;
  }

  private static BatchBuildResult Finished(IEnumerable<BatchBuildResult> results)
      => results.Single(r => r.Kind == BatchBuildResultKind.Finished);

  private static bool IsFutdHit(BatchBuildResult finished)
      => finished.Success == true
         && finished.Output?.Diagnostics.Any(d => d.Code == "ED-FUTD") == true;

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
  public async Task Deleting_Source_Triggers_Real_Build(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();
    var projDir = Path.GetDirectoryName(csproj)!;

    await RestoreAsync(h.Server, csproj);
    var first = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(first.Success);

    File.Delete(Path.Combine(projDir, "Class1.cs"));

    var third = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(third.Success);
    Assert.False(IsFutdHit(third), "build after source deletion must not be FUTD");
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task DesignTime_UpToDateCheckInput_Triggers_Real_Build(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject(
        extraPropertyGroupXml: """
    <CollectUpToDateCheckInputDesignTimeDependsOn>$(CollectUpToDateCheckInputDesignTimeDependsOn);CollectCustomFutdInputs</CollectUpToDateCheckInputDesignTimeDependsOn>
""");
    var projDir = Path.GetDirectoryName(csproj)!;
    var input = Path.Combine(projDir, "custom.input");
    File.WriteAllText(input, "v1");
    File.WriteAllText(csproj, File.ReadAllText(csproj).Replace("</Project>", """
<Target Name="CollectCustomFutdInputs">
  <ItemGroup>
    <UpToDateCheckInput Include="custom.input" />
  </ItemGroup>
</Target>
</Project>
"""));

    await RestoreAsync(h.Server, csproj);
    var first = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(first.Success);

    var second = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(second.Success);
    Assert.True(IsFutdHit(second));

    File.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddMinutes(1));

    var third = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(third.Success);
    Assert.False(IsFutdHit(third), "build after design-time UpToDateCheckInput touch must not be FUTD");
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task CopyToOutputDirectory_Destination_Missing_Triggers_Real_Build(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var csproj = h.CreateSingleTfmProject();
    var projDir = Path.GetDirectoryName(csproj)!;
    var contentDir = Path.Combine(projDir, "assets");
    Directory.CreateDirectory(contentDir);
    var content = Path.Combine(contentDir, "data.txt");
    File.WriteAllText(content, "v1");
    File.WriteAllText(csproj, File.ReadAllText(csproj).Replace("</Project>", """
<ItemGroup>
  <None Update="assets/data.txt" CopyToOutputDirectory="PreserveNewest" TargetPath="assets/data.txt" />
</ItemGroup>
</Project>
"""));

    await RestoreAsync(h.Server, csproj);
    var first = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(first.Success);

    var copied = Path.Combine(projDir, "bin", "Debug", "net8.0", "assets", "data.txt");
    Assert.True(File.Exists(copied), $"expected build to copy {copied}");

    var second = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(second.Success);
    Assert.True(IsFutdHit(second));

    File.Delete(copied);

    var third = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(third.Success);
    Assert.False(IsFutdHit(third), "build after CopyToOutputDirectory destination deletion must not be FUTD");
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task Missing_Built_Output_Collection_Declines_Futd(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);
    var dir = Path.Combine(h.WorkspaceDir, "NoBuiltCollection");
    Directory.CreateDirectory(dir);
    var csproj = Path.Combine(dir, "NoBuiltCollection.csproj");
    File.WriteAllText(csproj, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
    <EnableFastUpToDateCheck>true</EnableFastUpToDateCheck>
  </PropertyGroup>
</Project>
""");
    File.WriteAllText(Path.Combine(dir, "Class1.cs"), "namespace NoBuiltCollection; public class Class1 { }");

    await RestoreAsync(h.Server, csproj);
    var first = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(first.Success);

    var second = Finished(await BuildAsync(h.Server, csproj, "net8.0"));
    Assert.True(second.Success);
    Assert.False(IsFutdHit(second), "FUTD must be declined when built-output collection is unavailable");
  }

  [Theory]
  [MemberData(nameof(Targets))]
  public async Task ProjectReference_Without_Reference_Collection_Declines_Futd(string tfm, string exe, string[] leadingArgs)
  {
    _ = tfm;
    await using var h = await PropertyCacheHarness.StartAsync(exe, leadingArgs);

    var library = h.CreateSingleTfmProject(name: "Library");
    var appDir = Path.Combine(h.WorkspaceDir, "AppWithReference");
    Directory.CreateDirectory(appDir);
    var app = Path.Combine(appDir, "AppWithReference.csproj");
    File.WriteAllText(app, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
    <EnableFastUpToDateCheck>true</EnableFastUpToDateCheck>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Library\Library.csproj" />
  </ItemGroup>
  <Target Name="CollectUpToDateCheckBuiltDesignTime" Returns="@(UpToDateCheckBuilt)">
    <ItemGroup>
      <UpToDateCheckBuilt Include="$(TargetPath)" />
    </ItemGroup>
  </Target>
</Project>
""");
    File.WriteAllText(Path.Combine(appDir, "Class1.cs"), "namespace AppWithReference; public class Class1 { }");

    await RestoreAsync(h.Server, app);
    var first = Finished(await BuildAsync(h.Server, app, "net8.0"));
    Assert.True(first.Success);

    var second = Finished(await BuildAsync(h.Server, app, "net8.0"));
    Assert.True(second.Success);
    Assert.False(IsFutdHit(second), "FUTD must be declined for ProjectReference without resolved reference collection");
    Assert.True(File.Exists(library));
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
        new BatchBuildRequest(
            ProjectPaths: [csproj],
            Configuration: "Debug",
            TargetFramework: null,
            BuildTarget: "Build"));
    var results = new List<BatchBuildResult>();
    await foreach (var r in stream) { results.Add(r); }
    var second = Finished(results);

    Assert.True(second.Success);
    Assert.False(IsFutdHit(second), "FUTD must be skipped when TargetFramework is not set");
  }
}
