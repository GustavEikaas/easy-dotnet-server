using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.IDE.Utils;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Lsp;

public class LspController(IMsBuildService msBuildService, ILogger<LspController> logger) : BaseController
{
  public sealed record LspStartResponse(string Pipe);
  [JsonRpcMethod("lsp/start")]
  public async Task<LspStartResponse> StartLsp(bool useRoslynator = true, string[]? analyzerAssemblies = null, CancellationToken cancellationToken = default)
  {
    if (!msBuildService.HasMinimumSdk(new System.Version(9, 0)))
    {
      throw new System.Exception("Roslyn LSP requires .net 9 sdk or higher");
    }
    var pipeName = PipeUtils.GeneratePipeName();
    var proxy = new RoslynProxy(pipeName, logger);

    await proxy.StartAsync(new(useRoslynator, analyzerAssemblies));

    return new(pipeName);
  }
}