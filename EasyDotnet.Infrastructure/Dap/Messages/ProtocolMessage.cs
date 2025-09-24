using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap;

public static partial class DAP
{
  public class ProtocolMessage
  {
    public required int Seq { get; set; }
    public required string Type { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
  }
}