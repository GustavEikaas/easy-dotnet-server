using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.BuildServer;

public class BuildServerController(ILogger<BuildServerController> logger)
{
  [JsonRpcMethod("build-server/log")]
  public void Log(string message, int level)
  {
    switch (level)
    {
      case 2: logger.LogError($"[BuildServer] {message}"); break;
      case 1: logger.LogWarning($"[BuildServer] {message}"); break;
      case 3: logger.LogDebug($"[BuildServer] {message}"); break;
      case 0:
      default: logger.LogInformation($"[BuildServer] {message}"); break;
    }
  }
}