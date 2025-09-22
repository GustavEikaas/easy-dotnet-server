using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap;

public class ProtocolMessage
{
  public required int Seq { get; set; }
  public required string Type { get; set; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement> ExtraProperties { get; set; } = [];
}

public class Request : ProtocolMessage
{
  public required string Command { get; set; }
  public JsonElement? Arguments { get; set; }
}

public class Event : ProtocolMessage
{
  [JsonPropertyName("event")]
  public required string EventName { get; set; }
  public JsonElement? Body { get; set; }
}

public class Response : ProtocolMessage
{
  public required int RequestSeq { get; set; }
  public required bool Success { get; set; }
  public required string Command { get; set; }
  public string? Message { get; set; }
  public JsonElement? Body { get; set; }
}

public class ErrorResponse : Response
{
  public new required ErrorBody Body { get; set; }
}

public class ErrorBody
{
  public JsonElement? Error { get; set; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement>? ExtraProperties { get; set; } = [];
}