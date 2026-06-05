using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

namespace EasyDotnet.ContainerTests.Template;

/// <summary>
/// Verifies that the synthetic "add to solution" parameter is injected into template/parameters,
/// and that template/instantiate/v2 adds the new project to the solution when the value is "true".
/// </summary>
public abstract class TemplateInstantiateTests<TContainer> : TemplateInstantiateTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  [Fact]
  public async Task GetTemplateParameters_AlwaysIncludesAddToSolutionParam()
  {
    using var ws = new TempWorkspaceBuilder().WithSolutionX().Build();
    await InitializeWorkspaceAsync(ws);

    var template = await FindTemplateByShortNameAsync("console").WaitAsync(TimeSpan.FromMinutes(3));
    var parameters = await GetTemplateParametersAsync(template.Identity).WaitAsync(TimeSpan.FromMinutes(3));

    var addToSolutionParam = parameters.FirstOrDefault(p => p.Name == EasyDotnetAddToSolutionPostActionHandler.ParameterKey);
    Assert.NotNull(addToSolutionParam);
    Assert.Equal("bool", addToSolutionParam.DataType);
    Assert.Equal("true", addToSolutionParam.DefaultValue);
  }

  [Fact]
  public async Task ConsoleTemplate_AddToSolution_True_AddsProjectToSolution()
  {
    using var ws = new TempWorkspaceBuilder().WithSolutionX().Build();
    await InitializeWorkspaceAsync(ws);

    var template = await FindTemplateByShortNameAsync("console").WaitAsync(TimeSpan.FromMinutes(3));
    var outputPath = Path.Combine(ws.RootDir, "MyConsoleApp");
    var parameters = new Dictionary<string, string?>
    {
      [EasyDotnetAddToSolutionPostActionHandler.ParameterKey] = "true"
    };

    await InstantiateTemplateAsync(template.Identity, "MyConsoleApp", outputPath, parameters)
        .WaitAsync(TimeSpan.FromMinutes(3));

    var slnContent = await File.ReadAllTextAsync(ws.SolutionPath!);
    Assert.Contains("MyConsoleApp", slnContent);
  }

  [Fact]
  public async Task ConsoleTemplate_AddToSolution_False_DoesNotAddProjectToSolution()
  {
    using var ws = new TempWorkspaceBuilder().WithSolutionX().Build();
    await InitializeWorkspaceAsync(ws);

    var template = await FindTemplateByShortNameAsync("console").WaitAsync(TimeSpan.FromMinutes(3));
    var outputPath = Path.Combine(ws.RootDir, "MyConsoleApp");
    var parameters = new Dictionary<string, string?>
    {
      [EasyDotnetAddToSolutionPostActionHandler.ParameterKey] = "false"
    };

    await InstantiateTemplateAsync(template.Identity, "MyConsoleApp", outputPath, parameters)
        .WaitAsync(TimeSpan.FromMinutes(3));

    var slnContent = await File.ReadAllTextAsync(ws.SolutionPath!);
    Assert.DoesNotContain("MyConsoleApp", slnContent);
  }

  [Fact]
  public async Task ClasslibTemplate_AddToSolution_True_AddsProjectToSolution()
  {
    using var ws = new TempWorkspaceBuilder().WithSolutionX().Build();
    await InitializeWorkspaceAsync(ws);

    var template = await FindTemplateByShortNameAsync("classlib").WaitAsync(TimeSpan.FromMinutes(3));
    var outputPath = Path.Combine(ws.RootDir, "MyLibrary");
    var parameters = new Dictionary<string, string?>
    {
      [EasyDotnetAddToSolutionPostActionHandler.ParameterKey] = "true"
    };

    await InstantiateTemplateAsync(template.Identity, "MyLibrary", outputPath, parameters)
        .WaitAsync(TimeSpan.FromMinutes(3));

    var slnContent = await File.ReadAllTextAsync(ws.SolutionPath!);
    Assert.Contains("MyLibrary", slnContent);
  }

  [Fact]
  public async Task NoSolution_AddToSolution_True_DoesNotThrow()
  {
    // No solution — handler must be a silent no-op.
    using var ws = new TempWorkspaceBuilder().Build();
    await InitializeWorkspaceAsync(ws);

    var template = await FindTemplateByShortNameAsync("console").WaitAsync(TimeSpan.FromMinutes(3));
    var outputPath = Path.Combine(ws.RootDir, "MyConsoleApp");
    var parameters = new Dictionary<string, string?>
    {
      [EasyDotnetAddToSolutionPostActionHandler.ParameterKey] = "true"
    };

    // Should complete without throwing.
    await InstantiateTemplateAsync(template.Identity, "MyConsoleApp", outputPath, parameters)
        .WaitAsync(TimeSpan.FromMinutes(3));
  }
}

[Collection(ContainerCollections.Sdk8Linux)]
public sealed class TemplateInstantiateSdk8Linux : TemplateInstantiateTests<Sdk8LinuxContainer>;
[Collection(ContainerCollections.Sdk9Linux)]
public sealed class TemplateInstantiateSdk9Linux : TemplateInstantiateTests<Sdk9LinuxContainer>;
[Collection(ContainerCollections.Sdk10Linux)]
public sealed class TemplateInstantiateSdk10Linux : TemplateInstantiateTests<Sdk10LinuxContainer>;
