using EasyDotnet.Controllers;
using StreamJsonRpc;

namespace EasyDotnet.IDE.NewFile;

public class NewFileController(NewFileService newFileService) : BaseController
{
  //OBSOLETE endpoint name

  [JsonRpcMethod("json-code-gen-v2")]
  public async Task<BootstrapFileResultResponse> JsonToCode(string jsonData, string filePath, bool preferFileScopedNamespace)
  {
    var success = await newFileService.BootstrapFileFromJson(jsonData, filePath, preferFileScopedNamespace, new CancellationToken());
    return new(success);
  }

  [JsonRpcMethod("roslyn/bootstrap-file-v2")]
  public async Task<BootstrapFileResultResponse> BootstrapFile(string filePath, Kind kind, bool preferFileScopedNamespace)
  {
    var success = await newFileService.BootstrapFile(filePath, kind, preferFileScopedNamespace, new CancellationToken());
    return new(success);
  }


  #region newfile
  [JsonRpcMethod("new-file/bootstrap-file-v2", UseSingleObjectParameterDeserialization = true)]
  public async Task<BootstrapFileResultResponse> BootstrapFileNewFile(string filePath, Kind kind, bool preferFileScopedNamespace) =>
      await BootstrapFile(filePath, kind, preferFileScopedNamespace);


  [JsonRpcMethod("new-file/create-item", UseSingleObjectParameterDeserialization = true)]
  public async Task CreateItem(CreateItemRequest request, CancellationToken ct) =>
      await newFileService.CreateItem(request.OutputPath, request.PreferFileScopedNamespace, ct);


  [JsonRpcMethod("new-file/json-code-gen-v2", UseSingleObjectParameterDeserialization = true)]
  public async Task<BootstrapFileResultResponse> JsonToCodeNewFile(string jsonData, string filePath, bool preferFileScopedNamespace) =>
      await JsonToCode(jsonData, filePath, preferFileScopedNamespace);
  #endregion
}