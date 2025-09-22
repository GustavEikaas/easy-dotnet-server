using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap;

public class ProtocolMessage
{
  public int Seq { get; set; }
  public string Type { get; set; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement> ExtraProperties { get; set; }
}

public class Request : ProtocolMessage
{
  public string Command { get; set; }
  public JsonElement? Arguments { get; set; }
}

public class Event : ProtocolMessage
{
  [JsonPropertyName("event")]
  public string EventName { get; set; }
  public JsonElement? Body { get; set; }
}

public class Response : ProtocolMessage
{
  public int RequestSeq { get; set; }
  public bool Success { get; set; }
  public string Command { get; set; }
  public string Message { get; set; }
  public JsonElement? Body { get; set; }
}

public class ErrorResponse : Response
{
  public new ErrorBody Body { get; set; }
}

public class ErrorBody
{
  public JsonElement? Error { get; set; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement> ExtraProperties { get; set; }
}