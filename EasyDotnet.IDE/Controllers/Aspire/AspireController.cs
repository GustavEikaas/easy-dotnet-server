using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Infrastructure.Aspire.Server;
using EasyDotnet.Infrastructure.Dap;
using EasyDotnet.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Aspire;

public class AspireController(
  IMsBuildService msBuildService,
  INetcoreDbgService netcoreDbgService,
  IClientService clientService,
  ILogger<AspireController> logger,
  ILogger<DcpServer> dcpLogger,
  ILogger<DebuggerProxy> debuggerProxyLogger,
  ILogger<NetcoreDbgService> logger2) : BaseController
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
     netcoreDbgService,
     clientService,
     msBuildService,
     dcpLogger,
     debuggerProxyLogger,
     logger2,
     cancellationToken
   );

    logger.LogInformation("Aspire server infrastructure started successfully");
  }
}