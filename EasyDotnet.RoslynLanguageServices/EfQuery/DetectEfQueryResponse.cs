using System.Text.Json.Serialization;

namespace EasyDotnet.RoslynLanguageServices.EfQuery;

/// <summary>
/// When found, line/character (0-based) point at the start of the detected query expression so the
/// client can invoke the SQL endpoint with a position that is guaranteed to hit the query.
/// </summary>
public sealed record DetectEfQueryResponse(
    [property: JsonPropertyName("found")] bool Found,
    [property: JsonPropertyName("line")] int? Line,
    [property: JsonPropertyName("character")] int? Character)
{
  public static readonly DetectEfQueryResponse NotFound = new(false, null, null);
}