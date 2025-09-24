using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public record Response(int Seq, string Type, Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties, [property: JsonPropertyName("request_seq")] int RequestSeq, bool Success, string Command, string? Message) : ProtocolMessage(Seq, Type, AdditionalProperties);
}