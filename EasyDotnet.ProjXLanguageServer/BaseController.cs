using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer;

public class BaseController(JsonRpc jsonRpc)
{
  protected Task LogAsync(MessageType type, string message) =>
    jsonRpc.NotifyAsync("window/logMessage", new LogMessageParams
    {
      MessageType = type,
      Message = message
    });
}