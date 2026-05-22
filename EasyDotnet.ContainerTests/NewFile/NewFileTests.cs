using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;

namespace EasyDotnet.ContainerTests.NewFile;

public abstract class NewFileTests<TContainer> : NewFileTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task BootstrapFile_RazorComponent_GeneratesComponentTemplate()
  {
    using var ws = new TempWorkspaceBuilder().WithProject("MyApp").Build();
    await InitializeWorkspaceAsync(ws);

    var filePath = Path.Combine(ws.Project("MyApp").Dir, "Counter.razor");
    var result = await BeginBootstrapAsync(filePath);
    var text = await ReceiveAppliedTextAsync();

    Assert.True(result.Success);
    Assert.Contains("<h3>Counter</h3>", text);
    Assert.DoesNotContain("@code", text);
    Assert.DoesNotContain("namespace", text);
  }

  [Fact]
  public async Task BootstrapFile_RazorCodeBehind_GeneratesPartialClass()
  {
    using var ws = new TempWorkspaceBuilder().WithProject("MyApp").Build();
    await InitializeWorkspaceAsync(ws);

    var filePath = Path.Combine(ws.Project("MyApp").Dir, "Counter.razor.cs");
    var result = await BeginBootstrapAsync(filePath);
    var text = await ReceiveAppliedTextAsync();

    Assert.True(result.Success);
    Assert.Contains("public partial class Counter", text);
    Assert.Contains("MyApp", text); // namespace from project
    Assert.DoesNotContain("PageModel", text);
  }

  [Fact]
  public async Task BootstrapFile_CshtmlUnderPages_GeneratesRazorPageTemplate()
  {
    using var ws = new TempWorkspaceBuilder().WithProject("MyApp").Build();
    await InitializeWorkspaceAsync(ws);

    var pagesDir = Path.Combine(ws.Project("MyApp").Dir, "Pages");
    Directory.CreateDirectory(pagesDir);
    var filePath = Path.Combine(pagesDir, "Index.cshtml");

    var result = await BeginBootstrapAsync(filePath);
    var text = await ReceiveAppliedTextAsync();

    Assert.True(result.Success);
    Assert.StartsWith("@page", text);
    Assert.Contains("@model IndexModel", text);
    Assert.Contains("<h1>Index</h1>", text);
  }

  [Fact]
  public async Task BootstrapFile_CshtmlUnderViews_GeneratesMvcViewTemplate()
  {
    using var ws = new TempWorkspaceBuilder().WithProject("MyApp").Build();
    await InitializeWorkspaceAsync(ws);

    var viewsDir = Path.Combine(ws.Project("MyApp").Dir, "Views", "Home");
    Directory.CreateDirectory(viewsDir);
    var filePath = Path.Combine(viewsDir, "Index.cshtml");

    var result = await BeginBootstrapAsync(filePath);
    var text = await ReceiveAppliedTextAsync();

    Assert.True(result.Success);
    Assert.DoesNotContain("@page", text);
    Assert.Contains("ViewData[\"Title\"]", text);
    Assert.Contains("<h1>Index</h1>", text);
  }

  [Fact]
  public async Task BootstrapFile_CshtmlAtProjectRoot_GeneratesFallbackTemplate()
  {
    using var ws = new TempWorkspaceBuilder().WithProject("MyApp").Build();
    await InitializeWorkspaceAsync(ws);

    var filePath = Path.Combine(ws.Project("MyApp").Dir, "Widget.cshtml");
    var result = await BeginBootstrapAsync(filePath);
    var text = await ReceiveAppliedTextAsync();

    Assert.True(result.Success);
    Assert.DoesNotContain("@page", text);
    Assert.DoesNotContain("ViewData", text);
    Assert.Contains("<h1>Widget</h1>", text);
  }

  [Fact]
  public async Task BootstrapFile_CshtmlCodeBehind_GeneratesPageModelClass()
  {
    using var ws = new TempWorkspaceBuilder().WithProject("MyApp").Build();
    await InitializeWorkspaceAsync(ws);

    var pagesDir = Path.Combine(ws.Project("MyApp").Dir, "Pages");
    Directory.CreateDirectory(pagesDir);
    var filePath = Path.Combine(pagesDir, "Index.cshtml.cs");

    var result = await BeginBootstrapAsync(filePath);
    var text = await ReceiveAppliedTextAsync();

    Assert.True(result.Success);
    Assert.Contains("using Microsoft.AspNetCore.Mvc.RazorPages;", text);
    Assert.Contains("public class IndexModel : PageModel", text);
    Assert.DoesNotContain("partial", text);
  }

  [Fact]
  public async Task BootstrapFile_PlainCsClass_GeneratesNamespacedClass()
  {
    using var ws = new TempWorkspaceBuilder().WithProject("MyApp").Build();
    await InitializeWorkspaceAsync(ws);

    var filePath = Path.Combine(ws.Project("MyApp").Dir, "MyService.cs");
    var result = await BeginBootstrapAsync(filePath, kind: "Class");
    var text = await ReceiveAppliedTextAsync();

    Assert.True(result.Success);
    Assert.Contains("public class MyService", text);
    Assert.Contains("MyApp", text); // namespace
    Assert.DoesNotContain("partial", text);
  }

  [Fact]
  public async Task BootstrapFile_NonEmptyFile_ReturnsFalseAndDoesNotOverwrite()
  {
    using var ws = new TempWorkspaceBuilder().WithProject("MyApp").Build();
    await InitializeWorkspaceAsync(ws);

    var filePath = Path.Combine(ws.Project("MyApp").Dir, "Existing.razor");
    await File.WriteAllTextAsync(filePath, "<h1>Already here</h1>");

    var result = await BeginBootstrapAsync(filePath);

    Assert.False(result.Success);
    Assert.Equal("<h1>Already here</h1>", await File.ReadAllTextAsync(filePath));
  }
}

public sealed class NewFileSdk10Tests : NewFileTests<Sdk10LinuxContainer>;