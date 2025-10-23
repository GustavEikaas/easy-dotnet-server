using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server.Controllers;

public class CapabilitiesController
{
  [JsonRpcMethod("getCapabilities")]
  public string[] GetCapabilities(string token)
  {
    Console.WriteLine($"[{token}] GetCapabilities called");

    return ["baseline.v1", "project", "ms-dotnettools.csharp", "devkit", "ms-dotnettools.csdevkit"];
  }

  [JsonRpcMethod("hasCapability")]
  public bool HasCapability(string token, string capability)
  {
    Console.WriteLine($"[{token}] HasCapability called for: {capability}");
    // Return true/false based on supported capabilities
    return true;
  }
}