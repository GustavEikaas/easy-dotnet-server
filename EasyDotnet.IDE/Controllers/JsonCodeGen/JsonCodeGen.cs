using System.IO;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.IDE.Controllers.Roslyn;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.JsonCodeGen;

public class JsonCodeGen(IJsonCodeGenService jsonCodeGenService) : BaseController
{
  [JsonRpcMethod("json-code-gen")]
  public async Task<BootstrapFileResultResponse> JsonToCode(string jsonData, string filePath, bool preferFileScopedNamespace)
  {
    if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
    {
      return new(false);
    }

    var content = await jsonCodeGenService.ConvertJsonToCSharpCompilationUnit(jsonData, filePath, preferFileScopedNamespace);

    File.WriteAllText(filePath, content);
    return new(true);
  }

}