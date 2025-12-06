using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Infrastructure.Aspire.Server;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Aspire;

public class AspireController(
  IMsBuildService msBuildService,
  IDebugOrchestrator debugOrchestrator,
  INotificationService notificationService,
  IClientService clientService,
  ILogger<AspireController> logger,
  ILogger<DcpServer> dcpLogger) : BaseController
{
  [JsonRpcMethod("aspire/startDebugSession")]
  public async Task StartDebugger(string projectPath, CancellationToken cancellationToken)
  {

    var project = await msBuildService.GetOrSetProjectPropertiesAsync(projectPath, cancellationToken: cancellationToken);

    if (!project.IsAspireHost)
    {
      throw new System.Exception($"{Path.GetFileNameWithoutExtension(projectPath)} is not an aspire apphost");
    }

    logger.LogInformation("Starting Aspire AppHost {projectPath}", projectPath);

    await AspireServer.CreateAndStartAsync(
     projectPath,
     debugOrchestrator,
     notificationService,
     clientService,
     msBuildService,
     dcpLogger,
     cancellationToken
   );

    logger.LogInformation("Aspire server infrastructure started successfully");
  }
}