using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server.Controllers;

public class EditorController
{
  [JsonRpcMethod("openEditor")]
  public void OpenEditor(string token, string path)
  {
    Console.WriteLine($"[{token}] OpenEditor called for path: {path}");
  }
}