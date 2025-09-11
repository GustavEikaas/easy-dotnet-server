using System.IO;
using System.Threading.Tasks;
using EasyDotnet.Services;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.UserSecrets;

public class UserSecretsController(UserSecretsService userSecretsService) : BaseController
{
  [JsonRpcMethod("user-secrets/init")]
  public async Task<ProjectUserSecretsInitResponse> InitSecrets(string projectPath)
  {
    var secret = await userSecretsService.AddUserSecretsId(Path.GetFullPath(projectPath));
    return new(secret.Id, secret.FilePath);
  }

}