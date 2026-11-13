using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyDotnet.IDE.Mcp;

internal static class McpJson
{
  private static readonly JsonSerializerOptions Options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  };

  public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);
}