using EasyDotnet.BuildServer.Logging;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class ServerHandler(LogLevelState logLevelState, Logger logger)
{
  public sealed record SetLogLevelRequest(string Level);

  [JsonRpcMethod("_server/setLogLevel", UseSingleObjectParameterDeserialization = true)]
  public void SetLogLevel(SetLogLevelRequest request)
  {
    var parsed = LogLevelState.Parse(request.Level);
    logLevelState.Set(parsed);
    logger.LogInformation("BuildServer log level set to {Level}", parsed);
  }

  [JsonRpcMethod("_server/logdump")]
  public string[] LogDump() => [.. logLevelState.RingSink.Snapshot()];
}