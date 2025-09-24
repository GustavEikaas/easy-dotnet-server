using System.Text.Json;

namespace EasyDotnet.Infrastructure.Dap.Messages;

public partial class DAP
{
  public class Request : ProtocolMessage
  {
    public required string Command { get; set; }
    public JsonElement? Arguments { get; set; }
  }

}