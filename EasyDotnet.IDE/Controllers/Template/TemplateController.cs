using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Controllers.Template;
using EasyDotnet.IDE.Services;
using Microsoft.TemplateEngine.Utils;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Template;

public class TemplateController(TemplateEngineService templateEngineService, IEditorService editorService) : BaseController
{
  [JsonRpcMethod("template/list")]
  public async Task<IAsyncEnumerable<DotnetNewTemplateResponse>> GetTemplates()
  {
    await templateEngineService.EnsureInstalled();
    var templates = await templateEngineService.GetTemplatesAsync();

    return templates.Where(x => x.GetLanguage() != "VB").Select(x => new DotnetNewTemplateResponse(string.IsNullOrWhiteSpace(x.GetLanguage()) ? x.Name : $"{x.Name} ({x.GetLanguage()})", x.Name, x.Identity, x.GetTemplateType())).ToBatchedAsyncEnumerable(5);
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
      .ToBatchedAsyncEnumerable(5);
  }

  [JsonRpcMethod("template/instantiate")]
  public async Task InvokeTemplate(string identity, string name, string outputPath, Dictionary<string, string?>? parameters)
  {
    await templateEngineService.EnsureInstalled();
    await templateEngineService.InstantiateTemplateAsync(identity, name, outputPath, parameters);

    await OpenEntryPointIfApplicable(outputPath);
  }

  private async Task OpenEntryPointIfApplicable(string outputPath)
  {
    var programFile = Directory
        .EnumerateFiles(outputPath, "Program.cs", SearchOption.TopDirectoryOnly)
        .FirstOrDefault();

    if (programFile != null)
    {
      await editorService.RequestOpenBuffer(Path.GetFullPath(programFile), line: null);
    }
  }
}