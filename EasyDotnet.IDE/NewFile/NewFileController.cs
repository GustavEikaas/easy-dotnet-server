using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Controllers;
using StreamJsonRpc;

namespace EasyDotnet.IDE.NewFile;

public class NewFileController(NewFileService newFileService) : BaseController
{
  [JsonRpcMethod("roslyn/bootstrap-file-v2")]
  public async Task<BootstrapFileResultResponse> BootstrapFile(string filePath, Kind kind, bool preferFileScopedNamespace)
  {
    var success = await newFileService.BootstrapFile(filePath, kind, preferFileScopedNamespace, new CancellationToken());
    return new(success);
  }

  [JsonRpcMethod("json-code-gen-v2")]
  public async Task<BootstrapFileResultResponse> JsonToCode(string jsonData, string filePath, bool preferFileScopedNamespace)
  {
    var success = await newFileService.BootstrapFileFromJson(jsonData, filePath, preferFileScopedNamespace, new CancellationToken());
    return new(success);
  }
}
