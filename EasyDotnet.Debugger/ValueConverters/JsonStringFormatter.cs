using System.Text.Encodings.Web;
using System.Text.Json;

namespace EasyDotnet.Debugger.ValueConverters;

internal static class JsonStringFormatter
{
  private static readonly JsonSerializerOptions PrettyJsonOptions = new()
  {
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  private static readonly JsonSerializerOptions CompactJsonOptions = new()
  {
    WriteIndented = false,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  public static bool TryFormatJson(string value, out string prettyJson)
    => TryFormatJson(value, PrettyJsonOptions, out prettyJson);

  public static bool TryFormatJsonSingleLine(string value, out string compactJson)
    => TryFormatJson(value, CompactJsonOptions, out compactJson);

  private static bool TryFormatJson(string value, JsonSerializerOptions serializerOptions, out string formattedJson)
  {
    formattedJson = string.Empty;

    if (string.IsNullOrWhiteSpace(value))
    {
      return false;
    }

    var current = value.Trim();

    for (var i = 0; i < 3; i++)
    {
      try
      {
        using var json = JsonDocument.Parse(current);

        if (json.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
          formattedJson = JsonSerializer.Serialize(json.RootElement, serializerOptions);
          return true;
        }

        if (json.RootElement.ValueKind != JsonValueKind.String)
        {
          return false;
        }

        var inner = json.RootElement.GetString();
        if (string.IsNullOrWhiteSpace(inner))
        {
          return false;
        }

        current = inner;
      }
      catch (JsonException)
      {
        return false;
      }
    }

    return false;
  }
}
