using System.Text.Json.Serialization;

namespace EasyDotnet.RoslynLanguageServices.Rename;

public sealed record ShouldRenameFileRequest(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character,
    [property: JsonPropertyName("newName")] string NewName);