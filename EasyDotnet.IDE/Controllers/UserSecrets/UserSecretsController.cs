using System.IO;
using System.Threading.Tasks;
using EasyDotnet.Controllers;
using EasyDotnet.Controllers.UserSecrets;
using EasyDotnet.IDE.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.UserSecrets;

public class UserSecretsController(UserSecretsService userSecretsService) : BaseController
{
  [JsonRpcMethod("user-secrets/init")]
  public async Task<ProjectUserSecretsInitResponse> InitSecrets(string projectPath)
  {
#pragma warning disable CS0612 // Type or member is obsolete
    var secret = await userSecretsService.AddUserSecretsId(Path.GetFullPath(projectPath));
#pragma warning restore CS0612 // Type or member is obsolete
    return new(secret.Id, secret.FilePath);
  }

}