using System.Text.Json.Serialization;

namespace EasyDotnet.RoslynLanguageServices.CreateType;

public sealed record CreateTypeFromUsageRequest(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character);