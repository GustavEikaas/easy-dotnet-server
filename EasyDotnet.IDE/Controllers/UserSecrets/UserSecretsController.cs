using System.IO;
using System.Threading.Tasks;
using EasyDotnet.Controllers;
using EasyDotnet.Controllers.UserSecrets;
using EasyDotnet.Infrastructure.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.UserSecrets;

public class UserSecretsController(UserSecretsService userSecretsService) : BaseController
{
  [JsonRpcMethod("user-secrets/init")]
  public async Task<ProjectUserSecretsInitResponse> InitSecrets(string projectPath)
  {
    var secret = await userSecretsService.AddUserSecretsId(Path.GetFullPath(projectPath));
    return new(secret.Id, secret.FilePath);
  }

}