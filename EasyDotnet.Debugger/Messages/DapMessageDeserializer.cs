using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyDotnet.Debugger.Messages;

public static class DapMessageDeserializer
{
  private static readonly JsonSerializerOptions Options = new()
  {
    PropertyNameCaseInsensitive = true,
    Converters = { new InternalConverter(), new FlexibleIntConverter() }
  };

  /// <summary>
  /// Parses a JSON string into the correct ProtocolMessage subtype.
  /// Throws JsonException if parsing fails or required fields are missing.
  /// </summary>
  public static ProtocolMessage Parse(string json)
  {
    if (string.IsNullOrWhiteSpace(json))
    {
      throw new ArgumentNullException(nameof(json));
    }

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
      {
        throw new JsonException("Missing type property");
      }

      var type = typeProperty.GetString();
      if (type is null) throw new JsonException("Type property was null");

      return type switch
      {
        "request" => DeserializeRequest(root, options),
        "event" => JsonSerializer.Deserialize<Event>(root.GetRawText(), options)!,
        "response" => DeserializeResponse(root, options),
        _ => JsonSerializer.Deserialize<ProtocolMessage>(root.GetRawText(), options)!
      };
    }


    private static ProtocolMessage DeserializeResponse(JsonElement root, JsonSerializerOptions options)
    {
      if (root.TryGetProperty("success", out var successProp) &&
          successProp.ValueKind == JsonValueKind.False)
      {
        return JsonSerializer.Deserialize<ErrorResponse>(root.GetRawText(), options)!;
      }

      if (!root.TryGetProperty("command", out var cmdProp))
      {
        throw new JsonException(
            $"Successful DAP response missing required property 'command': {root.GetRawText()}"
        );
      }

      var cmd = cmdProp.GetString()?.ToLowerInvariant();

      return cmd switch
      {
        "variables" => JsonSerializer.Deserialize<VariablesResponse>(root.GetRawText(), options)!,
        _ => JsonSerializer.Deserialize<Response>(root.GetRawText(), options)!,
      };
    }

    private static ProtocolMessage DeserializeRequest(JsonElement root, JsonSerializerOptions options)
    {
      if (root.TryGetProperty("command", out var cmdProp))
      {
        var cmd = cmdProp.GetString();
        switch (cmd?.ToLowerInvariant())
        {
          case "attach":
            return JsonSerializer.Deserialize<InterceptableAttachRequest>(root.GetRawText(), options)!;
          case "variables":
            return JsonSerializer.Deserialize<InterceptableVariablesRequest>(root.GetRawText(), options)!;
          case "setbreakpoints":
            return JsonSerializer.Deserialize<SetBreakpointsRequest>(root.GetRawText(), options)!;
        }
      }

      return JsonSerializer.Deserialize<Request>(root.GetRawText(), options)!;
    }

    public override void Write(Utf8JsonWriter writer, ProtocolMessage value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
  }

  private class FlexibleIntConverter : JsonConverter<int>
  {
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
      JsonTokenType.Number => reader.GetInt32(),
      JsonTokenType.String when int.TryParse(reader.GetString(), out var val) => val,
      _ => throw new JsonException("Invalid number format")
    };

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
  }
}