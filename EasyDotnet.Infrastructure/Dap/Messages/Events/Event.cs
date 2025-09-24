using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public class Event : ProtocolMessage
  {
    [JsonPropertyName("event")]
    public required string EventName { get; set; }
  }
}