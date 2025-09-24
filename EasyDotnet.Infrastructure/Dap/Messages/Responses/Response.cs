using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public class Response : ProtocolMessage
  {
    [JsonPropertyName("request_seq")]
    public required int RequestSeq { get; set; }
    public required bool Success { get; set; }
    public required string Command { get; set; }
    public string? Message { get; set; }
  }
}