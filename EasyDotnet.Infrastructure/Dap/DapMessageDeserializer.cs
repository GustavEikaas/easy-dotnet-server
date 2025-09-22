using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap;

public static class DapMessageDeserializer
{
  private static readonly JsonSerializerOptions Options = new()
  {
    PropertyNameCaseInsensitive = true,
    Converters = { new InternalConverter() }
  };

  /// <summary>
  /// Parses a JSON string into the correct ProtocolMessage subtype.
  /// Throws JsonException if parsing fails or required fields are missing.
  /// </summary>
  public static ProtocolMessage Parse(string json)
  {
    if (string.IsNullOrWhiteSpace(json))
      throw new ArgumentNullException(nameof(json));

    var result = JsonSerializer.Deserialize<ProtocolMessage>(json, Options);

    return result is null ? throw new JsonException("Failed to deserialize JSON into ProtocolMessage.") : result;
  }

  private class InternalConverter : JsonConverter<ProtocolMessage>
  {
    public override ProtocolMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      using var doc = JsonDocument.ParseValue(ref reader);
      var root = doc.RootElement;

      if (!root.TryGetProperty("type", out var typeProperty))
        throw new JsonException("Missing type property");

      var type = typeProperty.GetString();

      if (string.IsNullOrWhiteSpace(type))
      {
        throw new JsonException("Type property is null or empty.");
      }

      return type switch
      {
        "request" => JsonSerializer.Deserialize<Request>(root.GetRawText(), options)
                      ?? throw new JsonException("Failed to deserialize Request."),
        "event" => JsonSerializer.Deserialize<Event>(root.GetRawText(), options)
                    ?? throw new JsonException("Failed to deserialize Event."),
        "response" => JsonSerializer.Deserialize<Response>(root.GetRawText(), options)
                       ?? throw new JsonException("Failed to deserialize Response."),
        _ => JsonSerializer.Deserialize<ProtocolMessage>(root.GetRawText(), options)
             ?? throw new JsonException("Failed to deserialize ProtocolMessage.")
      };
    }

    public override void Write(Utf8JsonWriter writer, ProtocolMessage value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
  }
}
