using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public record Event(
    int Seq,
    string Type,
    Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties,
    [property: JsonPropertyName("event")] string EventName
    ) : ProtocolMessage(Seq, Type, AdditionalProperties);
}