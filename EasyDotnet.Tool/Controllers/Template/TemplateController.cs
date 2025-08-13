using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyDotnet.Services;
using Microsoft.TemplateEngine.Utils;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.Template;

public sealed record DotnetNewTemplateResponse(string DisplayName, string Identity, string? Type);
public sealed record DotnetNewParameterResponse(string Name, string? DefaultValue, string? DefaultIfOptionWithoutValue, string DataType, string? Description, bool IsRequired, IReadOnlyDictionary<string, string>? Choices);
public class TemplateController(TemplateEngineService templateEngineService) : BaseController
{

  [JsonRpcMethod("template/list")]
  public async Task<IAsyncEnumerable<object>> GetTemplates()
  {
    await templateEngineService.EnsureInstalled();
    var templates = await templateEngineService.GetTemplatesAsync();

    return templates.Where(x => x.GetLanguage() == "C#").Select(x => new DotnetNewTemplateResponse(x.Name, x.Identity, x.GetTemplateType())).AsAsyncEnumerable();
    // return templates.AsAsyncEnumerable();
  }

  [JsonRpcMethod("template/parameters")]
  public async Task<IAsyncEnumerable<DotnetNewParameterResponse>> GetTemplateParameters(string identity)
  {
    await templateEngineService.EnsureInstalled();
    var parameters = await templateEngineService.GetTemplateOptions(identity);

    return parameters
      .Select(x => new DotnetNewParameterResponse(
            x.Name,
            x.DefaultValue,
            x.DefaultIfOptionWithoutValue,
            x.DataType,
            x.Description,
            x.Precedence.IsRequired,
            x.Choices?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DisplayName ?? kvp.Value.Description ?? "")))
      .AsAsyncEnumerable();
  }

  [JsonRpcMethod("template/instantiate")]
  public async Task InvokeTemplate(string identity, string name, string outputPath, Dictionary<string, string?>? parameters)
  {
    await templateEngineService.EnsureInstalled();
    await templateEngineService.InstantiateTemplateAsync(identity, name, outputPath, parameters);
  }
}