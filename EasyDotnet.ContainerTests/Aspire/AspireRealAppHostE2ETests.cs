using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using EasyDotnet.ContainerTests.Workspace.Run;

namespace EasyDotnet.ContainerTests.Aspire;

/// <summary>
/// Full end-to-end test of the Aspire DCP integration with a <em>real</em> Aspire AppHost and two
/// project resources. <c>workspace/run</c> on the AppHost builds it (restoring the real Aspire SDK
/// + dcp), spawns the Aspire host, and runs the AppHost via the editor. The AppHost starts dcp,
/// dcp connects back to the in-container DCP server and issues a <c>/run_session</c> for each
/// resource, which the server relays back as <c>runCommandManaged</c> — proving two project
/// resources are actually started through the editor.
///
/// <para>
/// Slow and network-dependent (downloads the Aspire SDK, dcp, and dashboard at build time), so it
/// targets a single SDK (.NET 10, matching the Aspire 13.x template TFM).
/// </para>
/// </summary>
public abstract class AspireRealAppHostE2ETests<TContainer> : AspireE2ETestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private const string AspireSdkVersion = "13.4.4";
  private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(8);

  [Fact]
  public async Task RealAppHost_StartsTwoProjectResourcesThroughDcp()
  {
    using var ws = BuildAspireWorkspace();
    await InitializeWorkspaceAsync(ws);

    // Point workspace/run straight at the AppHost (no solution → resolved from the file path),
    // so it routes through the DCP integration without a picker.
    var appHostProgram = Path.Combine(ws.Project("MyAppHost").Dir, "Program.cs");
    var runTask = Container.Rpc.WorkspaceRunAsync(filePath: appHostProgram);

    var startedResources = await WaitForResourceRunsAsync(["ApiService", "Web"], RunTimeout);

    Assert.Contains("ApiService", startedResources);
    Assert.Contains("Web", startedResources);

    // workspace/run returns once the AppHost has been launched; surface any build/launch failure.
    await runTask.WaitAsync(TimeSpan.FromMinutes(1));
  }

  private static TempWorkspace BuildAspireWorkspace()
  {
    // Solution-less workspace; files are overwritten with real Aspire content after scaffolding.
    var ws = new TempWorkspaceBuilder()
      .WithProject("ApiService")
      .WithProject("Web")
      .WithProject("MyAppHost")
      .Build();

    WriteWebProject(ws.Project("ApiService"));
    WriteWebProject(ws.Project("Web"));
    WriteAppHost(ws.Project("MyAppHost"));
    return ws;
  }

  private static void WriteWebProject(TempProject project)
  {
    project.WriteFile($"{project.Name}.csproj", """
      <Project Sdk="Microsoft.NET.Sdk.Web">
        <PropertyGroup>
          <TargetFramework>net10.0</TargetFramework>
          <ImplicitUsings>enable</ImplicitUsings>
          <Nullable>enable</Nullable>
        </PropertyGroup>
      </Project>
      """);

    project.WriteProgram("""
      var builder = WebApplication.CreateBuilder(args);
      var app = builder.Build();
      app.MapGet("/", () => "ok");
      app.Run();
      """);
  }

  private static void WriteAppHost(TempProject project)
  {
    project.WriteFile($"{project.Name}.csproj", $"""
      <Project Sdk="Aspire.AppHost.Sdk/{AspireSdkVersion}">
        <PropertyGroup>
          <OutputType>Exe</OutputType>
          <TargetFramework>net10.0</TargetFramework>
          <ImplicitUsings>enable</ImplicitUsings>
          <Nullable>enable</Nullable>
          <IsAspireHost>true</IsAspireHost>
          <UserSecretsId>aspire-e2e-test</UserSecretsId>
        </PropertyGroup>
        <ItemGroup>
          <ProjectReference Include="../ApiService/ApiService.csproj" />
          <ProjectReference Include="../Web/Web.csproj" />
        </ItemGroup>
      </Project>
      """);

    // Two project resources, no dependencies/health checks so dcp issues both run_sessions promptly.
    project.WriteProgram("""
      var builder = DistributedApplication.CreateBuilder(args);
      builder.AddProject<Projects.ApiService>("apiservice");
      builder.AddProject<Projects.Web>("web");
      builder.Build().Run();
      """);
  }
}

public sealed class AspireRealAppHostE2ESdk10Linux : AspireRealAppHostE2ETests<Sdk10LinuxContainer>;