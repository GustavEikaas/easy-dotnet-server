using EasyDotnet.Controllers;
using EasyDotnet.IDE.Picker.Models;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Picker;

public sealed class PickerController : BaseController
{
  [JsonRpcMethod("picker/query")]
  public Task<PickerChoice[]> Query(Guid guid, string query, CancellationToken ct)
      => throw new NotImplementedException();

  [JsonRpcMethod("picker/preview")]
  public Task<PreviewResult?> Preview(Guid guid, string itemId, CancellationToken ct)
      => throw new NotImplementedException();
}