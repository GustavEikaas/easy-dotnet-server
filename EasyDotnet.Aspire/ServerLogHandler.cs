using EasyDotnet.Aspire.Logging;
using StreamJsonRpc;

namespace EasyDotnet.Aspire;

/// <summary>
/// RPC target exposing the Aspire host's logs to the IDE, mirroring the BuildServer's
/// <c>_server/logdump</c> / <c>_server/setLogLevel</c> contract.
/// </summary>
public sealed class ServerLogHandler(RingLogState state)
{
  public sealed record SetLogLevelRequest(string Level);

  [JsonRpcMethod("_server/logdump")]
  public string[] LogDump() => [.. state.Sink.Snapshot()];

  [JsonRpcMethod("_server/setLogLevel", UseSingleObjectParameterDeserialization = true)]
  public void SetLogLevel(SetLogLevelRequest request) => state.SetLevel(RingLogState.Parse(request.Level));
}