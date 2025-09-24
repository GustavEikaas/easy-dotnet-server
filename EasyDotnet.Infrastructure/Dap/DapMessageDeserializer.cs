using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap;

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
  public static DAP.ProtocolMessage Parse(string json)
  {
    if (string.IsNullOrWhiteSpace(json))
    {
      throw new ArgumentNullException(nameof(json));
    }

    var result = JsonSerializer.Deserialize<DAP.ProtocolMessage>(json, Options);

    return result is null ? throw new JsonException("Failed to deserialize JSON into ProtocolMessage.") : result;
  }

  private class InternalConverter : JsonConverter<DAP.ProtocolMessage>
  {
    public override DAP.ProtocolMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
        "event" => JsonSerializer.Deserialize<DAP.Event>(root.GetRawText(), options)!,
        "response" => DeserializeResponse(root, options),
        _ => JsonSerializer.Deserialize<DAP.ProtocolMessage>(root.GetRawText(), options)!
      };
    }


    private static DAP.Response DeserializeResponse(JsonElement root, JsonSerializerOptions options)
    {
      if (root.TryGetProperty("command", out var cmdProp))
      {
        var cmd = cmdProp.GetString();

        switch (cmd?.ToLowerInvariant())
        {
          case "variables":
            return JsonSerializer.Deserialize<DAP.VariablesResponse>(root.GetRawText(), options)!;
        }
      }

      return JsonSerializer.Deserialize<DAP.Response>(root.GetRawText(), options)!;
    }

    private static DAP.Request DeserializeRequest(JsonElement root, JsonSerializerOptions options)
    {
      if (root.TryGetProperty("command", out var cmdProp))
      {
        var cmd = cmdProp.GetString();

        switch (cmd?.ToLowerInvariant())
        {
          case "attach":
            return JsonSerializer.Deserialize<DAP.InterceptableAttachRequest>(root.GetRawText(), options)!;

          case "setbreakpoints":
            return JsonSerializer.Deserialize<DAP.SetBreakpointsRequest>(root.GetRawText(), options)!;

          case "variables":
            return JsonSerializer.Deserialize<DAP.VariablesRequest>(root.GetRawText(), options)!;
        }
      }

      return JsonSerializer.Deserialize<DAP.Request>(root.GetRawText(), options)!;
    }

    public override void Write(Utf8JsonWriter writer, DAP.ProtocolMessage value, JsonSerializerOptions options) =>
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