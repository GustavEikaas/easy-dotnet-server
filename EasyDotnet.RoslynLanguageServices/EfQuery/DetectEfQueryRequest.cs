using System.Text.Json.Serialization;

namespace EasyDotnet.RoslynLanguageServices.EfQuery;

public sealed record DetectEfQueryRequest(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character);