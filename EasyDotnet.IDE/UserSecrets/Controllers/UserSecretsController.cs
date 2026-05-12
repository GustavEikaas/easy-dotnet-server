using EasyDotnet.Controllers;
using EasyDotnet.IDE.UserSecrets.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.UserSecrets.Controllers;

public class UserSecretsController(UserSecretsFlowService userSecretsFlowService) : BaseController
{
  [JsonRpcMethod("user-secrets/open")]
  public async Task OpenSecrets(CancellationToken ct) =>
      await userSecretsFlowService.OpenSecretsAsync(ct);
}
