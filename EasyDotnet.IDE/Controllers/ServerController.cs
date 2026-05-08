using EasyDotnet.Controllers;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Logging;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers;

public class ServerController(
    LogLevelState logLevelState,
    IBuildHostManager buildHostManager,
    ILogger<ServerController> logger) : BaseController
{
  public sealed record SetLogLevelRequest(string Level);

  [JsonRpcMethod("_server/setLogLevel", UseSingleObjectParameterDeserialization = true)]
  public async Task SetLogLevel(SetLogLevelRequest request, CancellationToken ct)
  {
    var parsed = LogLevelState.Parse(request.Level);
    logLevelState.Set(parsed);
    logger.LogInformation("Log level set to {Level}", parsed);
    await buildHostManager.SetLogLevelAsync(parsed.ToString(), ct);
  }

  [JsonRpcMethod("_server/logdump")]
  public string[] LogDump() => [.. logLevelState.RingSink.Snapshot()];

  [JsonRpcMethod("_server/logdump/buildserver")]
  public Task<string[]> LogDumpBuildServer(CancellationToken ct) =>
      buildHostManager.GetLogsAsync(ct);
}