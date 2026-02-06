using StreamJsonRpc;

namespace EasyDotnet.BuildServer;

public interface IIdeLogger
{
  [JsonRpcMethod("build-server/log")]
  Task LogAsync(string message, int level);
}
