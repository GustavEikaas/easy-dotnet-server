namespace EasyDotnet.BuildServer.MsBuildProject;
/// <summary>
/// Parses #: directives from .cs files for single-file app configuration.
/// Scans lines until first non-directive line.
/// </summary>
public static class CSharpDirectiveParser
{
  public record ParsedDirective(string Type, string Name, string? Version = null, string? Value = null);
  /// <summary>
  /// Parses #: directives from source file content.
  /// Stops at first non-directive line (not #:, not #!, not blank/comment-only).
  /// </summary>
  public static List<ParsedDirective> Parse(string sourceContent)
  {
    var directives = new List<ParsedDirective>();
    foreach (var line in sourceContent.Split([Environment.NewLine], StringSplitOptions.None))
    {
      var trimmed = line.Trim();
      if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
      {
        continue;
      }
      if (!trimmed.StartsWith("#:") && !trimmed.StartsWith("#!"))
      {
        break;
      }
      if (trimmed.StartsWith("#!"))
      {
        continue;
      }
      if (trimmed.StartsWith("#:"))
      {
        var parts = trimmed[2..].Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
          switch (parts[0].ToLowerInvariant())
          {
            case "sdk":
              if (parts.Length > 1)
              {
                var nameAndVersion = parts[1].Split('@');
                directives.Add(new ParsedDirective(
                    "sdk",
                    nameAndVersion[0],
                    nameAndVersion.Length > 1 ? nameAndVersion[1] : null));
              }
              break;
            case "package":
              if (parts.Length > 1)
              {
                var nameAndVersion = parts[1].Split('@');
                directives.Add(new ParsedDirective(
                    "package",
                    nameAndVersion[0],
                    nameAndVersion.Length > 1 ? nameAndVersion[1] : null));
              }
              break;
            case "property":
              if (parts.Length > 1)
              {
                var propParts = parts[1].Split('=');
                directives.Add(new ParsedDirective(
                    "property",
                    propParts[0],
                    null,
                    propParts.Length > 1 ? propParts[1] : ""));
              }
              break;
            case "project":
              directives.Add(new ParsedDirective(
                  "project",
                  parts.Length > 1 ? parts[1] : "",
                  null));
              break;
          }
        }
      }
    }
    return directives;
  }
}