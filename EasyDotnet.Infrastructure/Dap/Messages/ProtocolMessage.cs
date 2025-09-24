using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public record ProtocolMessage(int Seq, string Type, [property: JsonExtensionData] Dictionary<string, JsonElement>? AdditionalProperties);
}