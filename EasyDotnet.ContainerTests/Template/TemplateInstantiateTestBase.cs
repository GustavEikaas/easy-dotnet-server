using EasyDotnet.ContainerTests.Docker;

namespace EasyDotnet.ContainerTests.Template;

/// <summary>
/// Base class for template instantiation container tests.
/// Provides helpers for calling template/list, template/parameters, and template/instantiate/v2.
/// </summary>
public abstract class TemplateInstantiateTestBase<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  protected async Task<TestTemplateInfo> FindTemplateByShortNameAsync(string shortName, CancellationToken cancellationToken = default)
  {
    var stream = await Container.Rpc.InvokeAsync<IAsyncEnumerable<TestTemplateInfo>>("template/list");
    await foreach (var t in stream.WithCancellation(cancellationToken))
    {
      if (t.Name.Contains(shortName, StringComparison.OrdinalIgnoreCase) ||
          t.DisplayName.Contains(shortName, StringComparison.OrdinalIgnoreCase) ||
          t.Identity.Contains(shortName, StringComparison.OrdinalIgnoreCase))
      {
        return t;
      }
    }
    throw new InvalidOperationException($"Template '{shortName}' not found in template list");
  }

  protected async Task<List<TestTemplateParameter>> GetTemplateParametersAsync(string identity, CancellationToken cancellationToken = default)
  {
    var stream = await Container.Rpc.InvokeWithParameterObjectAsync<IAsyncEnumerable<TestTemplateParameter>>(
        "template/parameters", new { identity });
    var result = new List<TestTemplateParameter>();
    await foreach (var p in stream.WithCancellation(cancellationToken))
    {
      result.Add(p);
    }
    return result;
  }

  protected Task InstantiateTemplateAsync(
      string identity,
      string name,
      string outputPath,
      Dictionary<string, string?> parameters,
      CancellationToken cancellationToken = default)
    => Container.Rpc.InvokeWithParameterObjectAsync(
        "template/instantiate/v2",
        new { identity, name, outputPath, parameters },
        cancellationToken);
}

public sealed record TestTemplateInfo(string DisplayName, string Name, string Identity, string? Type, bool IsNameRequired);
public sealed record TestTemplateParameter(string Name, string? DefaultValue, string? DataType, string? Description, bool IsRequired);