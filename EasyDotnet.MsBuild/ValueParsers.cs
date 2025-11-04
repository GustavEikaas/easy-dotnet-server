namespace EasyDotnet.MsBuild;

public static class MsBuildValueParsers
{
  public static string? AsString(IReadOnlyDictionary<string, string> values, string name)
      => values.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

  public static bool AsBool(IReadOnlyDictionary<string, string> values, string name)
      => values.TryGetValue(name, out var v) && bool.TryParse(v, out var b) && b;

  public static string[] AsStringList(IReadOnlyDictionary<string, string> values, string name)
      => values.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
          ? v.Split(";", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          : [];

  public static int? AsInt(IReadOnlyDictionary<string, string> values, string name)
      => values.TryGetValue(name, out var v) && int.TryParse(v, out var i) ? i : null;

  public static Version? AsVersion(IReadOnlyDictionary<string, string> values, string name)
      => values.TryGetValue(name, out var v) && Version.TryParse(v, out var ver) ? ver : null;

  public static string? AsPath(IReadOnlyDictionary<string, string> values, string name)
      => values.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
          ? v.Replace("\\", "/")
          : null;
}