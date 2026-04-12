using EasyDotnet.Controllers;
using EasyDotnet.IDE.Picker.Models;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Picker;

public sealed class PickerController(IPickerScopeRegistry registry) : BaseController
{
  [JsonRpcMethod("picker/query")]
  public async Task<PickerChoice[]> Query(Guid guid, string query, CancellationToken ct)
  {
    if (registry.Get(guid) is not ILivePickerScope liveScope)
      return [];
    return await liveScope.QueryAsync(query, ct);
  }

  [JsonRpcMethod("picker/preview")]
  public Task<PreviewResult?> Preview(Guid guid, string itemId, CancellationToken ct)
  {
    var scope = registry.Get(guid);
    if (scope is null) return Task.FromResult<PreviewResult?>(null);
    return scope.GetPreviewAsync(itemId, ct);
  }
}