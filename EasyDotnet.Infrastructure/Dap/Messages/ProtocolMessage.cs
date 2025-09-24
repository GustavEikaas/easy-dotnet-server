using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap.Messages;

public partial class DAP
{
  public class ProtocolMessage
  {
    public required int Seq { get; set; }
    public required string Type { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtraProperties { get; set; } = [];
  }
}
