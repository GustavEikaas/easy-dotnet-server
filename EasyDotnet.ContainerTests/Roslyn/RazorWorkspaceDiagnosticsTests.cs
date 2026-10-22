using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using EasyDotnet.IDE.Controllers.Roslyn;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Roslyn;

/// <summary>
/// <para>
/// Verifies <c>roslyn/get-workspace-diagnostics</c> runs the SDK's Razor source generator, so
/// component code-behind files (<c>*.razor.cs</c>) see the generated half of their partial class.
/// </para>
/// <para>
/// Root cause: the server opens the solution in its own MSBuildWorkspace and loads the SDK's Razor
/// generator into its own Roslyn. SDK 10.0.3xx ships a generator built against Roslyn 5.5 while the
/// server shipped Roslyn 5.3, so the generator failed to load and every code-behind reported
/// CS0103 (InvokeAsync, StateHasChanged) and CS0115 (no suitable method found to override) even
/// though <c>dotnet build</c> succeeded.
/// </para>
/// <para>
/// #GustavEikaas/easy-dotnet.nvim#1077
/// </para>
/// </summary>
public abstract class RazorWorkspaceDiagnosticsTests<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private const string ProjectName = "BlazorLib";

  [Fact]
  public async Task WorkspaceDiagnostics_RazorCodeBehind_SeesGeneratedComponent()
  {
    using var ws = CreateRazorWorkspace();
    await RestoreAsync(ws);
    await InitializeWorkspaceAsync(ws);

    var diagnostics = await GetWorkspaceDiagnosticsAsync(ws.SolutionPath!);

    Assert.Empty(diagnostics.Select(Format));
  }

  [Fact]
  public async Task WorkspaceDiagnostics_RazorProjectWithRealError_ReportsOnlyThatError()
  {
    using var ws = CreateRazorWorkspace();
    ws.Project(ProjectName).WriteFile("Broken.cs", """
      namespace BlazorLib;

      public static class Broken
      {
        public static int Value => UndefinedVariable;
      }
      """);
    await RestoreAsync(ws);
    await InitializeWorkspaceAsync(ws);

    var diagnostics = await GetWorkspaceDiagnosticsAsync(ws.SolutionPath!);

    var error = Assert.Single(diagnostics, d => d.Severity == 1);
    Assert.Equal("CS0103", error.Code);
    Assert.EndsWith("Broken.cs", error.FilePath);
  }

  private static TempWorkspace CreateRazorWorkspace()
  {
    var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject(ProjectName)
      .Build();

    var project = ws.Project(ProjectName);
    project.WriteFile($"{ProjectName}.csproj", """
      <Project Sdk="Microsoft.NET.Sdk.Razor">
        <PropertyGroup>
          <TargetFramework>net10.0</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
        </PropertyGroup>
        <ItemGroup>
          <FrameworkReference Include="Microsoft.AspNetCore.App" />
        </ItemGroup>
      </Project>
      """);
    project.WriteFile("_Imports.razor", """
      @using Microsoft.AspNetCore.Components
      @using BlazorLib.Components
      """);

    // Component with a code-behind that only compiles against the generated ComponentBase half.
    project.WriteFile("Components/ProductCard.razor", """
      <p>@Count</p>
      """);
    project.WriteFile("Components/ProductCard.razor.cs", """
      namespace BlazorLib.Components;

      public partial class ProductCard
      {
        private int Count;

        protected override void OnInitialized() => Count = 1;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
          if (!firstRender)
            return;

          Count++;
          await InvokeAsync(StateHasChanged);
        }
      }
      """);

    // Page whose code-behind references another component type, which only exists once generated.
    project.WriteFile("Pages/Products.razor", """
      @page "/products"
      <ProductCard @ref="_card" />
      """);
    project.WriteFile("Pages/Products.razor.cs", """
      using BlazorLib.Components;

      namespace BlazorLib.Pages;

      public partial class Products
      {
        private ProductCard? _card;

        protected override Task OnAfterRenderAsync(bool firstRender) =>
          _card is null ? Task.CompletedTask : InvokeAsync(StateHasChanged);
      }
      """);

    return ws;
  }

  private async Task RestoreAsync(TempWorkspace ws)
  {
    var result = await Container.ExecAsync(["dotnet", "restore", ws.SolutionPath!]);
    Assert.True(result.ExitCode == 0, $"dotnet restore failed:\n{result.Stdout}\n{result.Stderr}");
  }

  private async Task<List<DiagnosticMessage>> GetWorkspaceDiagnosticsAsync(string targetPath)
  {
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

    var stream = await Container.Rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<DiagnosticMessage>>(
      "roslyn/get-workspace-diagnostics",
      new { targetPath, includeWarnings = false },
      cts.Token);

    var diagnostics = new List<DiagnosticMessage>();
    await foreach (var diagnostic in stream.WithCancellation(cts.Token))
      diagnostics.Add(diagnostic);
    return diagnostics;
  }

  private static string Format(DiagnosticMessage d) =>
    $"{Path.GetFileName(d.FilePath)}:{d.Range.Start.Line + 1} {d.Code}: {d.Message}";
}

public sealed class RazorWorkspaceDiagnosticsSdk10Tests : RazorWorkspaceDiagnosticsTests<Sdk10LinuxContainer>;

public sealed class RazorWorkspaceDiagnosticsSdk10Band3Tests : RazorWorkspaceDiagnosticsTests<Sdk10Band3LinuxContainer>;
