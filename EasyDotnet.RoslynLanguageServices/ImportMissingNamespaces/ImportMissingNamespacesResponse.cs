using System.Text.Json.Serialization;

namespace EasyDotnet.RoslynLanguageServices.ImportMissingNamespaces;

public sealed record ImportMissingNamespacesResponse(
    [property: JsonPropertyName("canImport")] bool CanImport,
    [property: JsonPropertyName("usings")] string[] Usings,
    [property: JsonPropertyName("reason")] string? Reason)
{
  public static ImportMissingNamespacesResponse No(string reason) =>
      new(false, [], reason);

  public static ImportMissingNamespacesResponse Yes(string[] usings) =>
      new(true, usings, "Resolved missing namespaces.");
}