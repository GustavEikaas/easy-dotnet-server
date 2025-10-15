using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server.Controllers;

public class LoggingController
{
  [JsonRpcMethod("logMessage")]
  public void LogMessage(string token, string logLevel, string message)
  {
    Console.WriteLine($"[{token}] LogMessage ({logLevel}): {message}");
  }
}
