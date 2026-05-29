using System.Text.Json.Serialization;

namespace EasyDotnet.RoslynLanguageServices.Rename;

public sealed record ShouldRenameFileResponse(
    [property: JsonPropertyName("shouldRename")] bool ShouldRename,
    [property: JsonPropertyName("oldUri")] string? OldUri,
    [property: JsonPropertyName("newUri")] string? NewUri,
    [property: JsonPropertyName("reason")] string Reason)
{
  public static ShouldRenameFileResponse No(string reason) => new(false, null, null, reason);

  public static ShouldRenameFileResponse Yes(string oldPath, string newPath, string reason) =>
      new(true, PathToUri(oldPath), PathToUri(newPath), reason);

  private static string PathToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;
}