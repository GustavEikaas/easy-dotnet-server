using System;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Controllers;
using EasyDotnet.IDE.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Lsp;

public class LspController(ILogger<LspController> logger) : BaseController
{
  public sealed record LspStartResponse(string Pipe);
  [JsonRpcMethod("lsp/start")]
  public async Task<LspStartResponse> StartLsp(bool useRoslynator, List<string> analyzerAssemblies, CancellationToken cancellationToken)
  {
    var pipeName = PipeUtils.GeneratePipeName();
    var proxy = new RoslynProxy(pipeName, logger);

    _ = Task.Run(async () => await proxy.StartAsync(), cancellationToken);

    return new(pipeName);
  }
}