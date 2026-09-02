using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyDotnet.Debugger.Services;

public sealed class SourcePathMapper
{
  // NetCoreDbg indexes source paths using the debugger host's case rules; this proxy runs on that same host.
  private readonly StringComparer _prefixComparer = OperatingSystem.IsWindows()
    ? StringComparer.OrdinalIgnoreCase
    : StringComparer.Ordinal;
  private readonly StringComparison _prefixComparison = OperatingSystem.IsWindows()
    ? StringComparison.OrdinalIgnoreCase
    : StringComparison.Ordinal;
  private PathMapping[] _debuggerMappings = [];
  private PathMapping[] _clientMappings = [];

  public void Configure(IReadOnlyDictionary<string, string> sourceFileMap)
  {
    ArgumentNullException.ThrowIfNull(sourceFileMap);

    var validator = CreateValidator();
    foreach (var entry in sourceFileMap)
    {
      validator.Add(entry.Key, entry.Value);
    }

    validator.ApplyTo(this);
  }

  public Validator CreateValidator() => new(_prefixComparer, _prefixComparison);

  public string MapDebuggerToClient(string path) => MapPath(
    path,
    _debuggerMappings,
    static mapping => mapping.DebuggerPrefix,
    static mapping => mapping.ClientPrefix,
    static mapping => mapping.ClientSeparator);

  public string MapClientToDebugger(string path) => MapPath(
    path,
    _clientMappings,
    static mapping => mapping.ClientPrefix,
    static mapping => mapping.DebuggerPrefix,
    static mapping => mapping.DebuggerSeparator);

  public JsonElement RewriteClientSources(JsonElement element) => RewriteSources(element, MapClientToDebugger);

  public JsonElement RewriteDebuggerSources(JsonElement element) => RewriteSources(element, MapDebuggerToClient);

  private string MapPath(
    string path,
    PathMapping[] mappings,
    Func<PathMapping, string> sourcePrefixSelector,
    Func<PathMapping, string> destinationPrefixSelector,
    Func<PathMapping, char> destinationSeparatorSelector)
  {
    var normalizedPath = NormalizePath(path);
    foreach (var mapping in mappings)
    {
      var sourcePrefix = sourcePrefixSelector(mapping);
      if (IsPrefixMatch(normalizedPath, sourcePrefix))
      {
        return ReplacePrefix(
          normalizedPath,
          sourcePrefix,
          destinationPrefixSelector(mapping),
          destinationSeparatorSelector(mapping));
      }
    }

    return path;
  }

  private bool IsPrefixMatch(string path, string prefix)
    => IsPrefixMatch(path, prefix, _prefixComparison);

  private static bool IsPrefixMatch(string path, string prefix, StringComparison prefixComparison)
  {
    if (path.Equals(prefix, prefixComparison))
    {
      return true;
    }
    if (prefix == "/")
    {
      return path.StartsWith("/", prefixComparison);
    }

    return path.Length > prefix.Length
      && path.StartsWith(prefix, prefixComparison)
      && path[prefix.Length] == '/';
  }

  private static int GetNestingRelationship(
      string firstPrefix,
      string secondPrefix,
      StringComparison prefixComparison)
  {
    if (IsPrefixMatch(secondPrefix, firstPrefix, prefixComparison))
    {
      return 1;
    }
    if (IsPrefixMatch(firstPrefix, secondPrefix, prefixComparison))
    {
      return -1;
    }

    return 0;
  }

  private static string ReplacePrefix(string path, string sourcePrefix, string destinationPrefix, char destinationSeparator)
  {
    var suffixStart = sourcePrefix == "/" ? 1 : sourcePrefix.Length;
    var suffix = path[suffixStart..].TrimStart('/').Replace('/', destinationSeparator);
    var destination = destinationPrefix.Replace('/', destinationSeparator);

    if (suffix.Length == 0)
    {
      return destination;
    }

    return destination.EndsWith(destinationSeparator)
      ? destination + suffix
      : destination + destinationSeparator + suffix;
  }

  private JsonElement RewriteSources(JsonElement element, Func<string, string> mapPath)
  {
    if (_debuggerMappings.Length == 0)
    {
      return element;
    }

    var root = JsonNode.Parse(element.GetRawText())!;
    RewriteContainedSources(root, mapPath);
    return JsonSerializer.SerializeToElement(root);
  }

  private static void RewriteContainedSources(JsonNode? node, Func<string, string> mapPath)
  {
    switch (node)
    {
      case JsonObject obj:
        foreach (var property in obj)
        {
          if (property.Key.Equals("source", StringComparison.OrdinalIgnoreCase) && property.Value is JsonObject source)
          {
            RewriteSource(source, mapPath);
          }
          else if (property.Key.Equals("sources", StringComparison.OrdinalIgnoreCase) && property.Value is JsonArray sources)
          {
            foreach (var item in sources)
            {
              if (item is JsonObject nestedSource)
              {
                RewriteSource(nestedSource, mapPath);
              }
            }
          }
          else if (IsSourceContainer(property.Key))
          {
            RewriteContainedSources(property.Value, mapPath);
          }
          else if (property.Key.Equals("instructions", StringComparison.OrdinalIgnoreCase) && property.Value is JsonArray instructions)
          {
            RewriteDisassembledInstructionLocations(instructions, mapPath);
          }
        }
        break;
      case JsonArray array:
        foreach (var item in array)
        {
          RewriteContainedSources(item, mapPath);
        }
        break;
    }
  }

  private static bool IsSourceContainer(string propertyName) =>
    propertyName.Equals("stackFrames", StringComparison.OrdinalIgnoreCase)
    || propertyName.Equals("scopes", StringComparison.OrdinalIgnoreCase)
    || propertyName.Equals("breakpoints", StringComparison.OrdinalIgnoreCase)
    || propertyName.Equals("breakpoint", StringComparison.OrdinalIgnoreCase);

  private static void RewriteDisassembledInstructionLocations(JsonArray instructions, Func<string, string> mapPath)
  {
    foreach (var instruction in instructions.OfType<JsonObject>())
    {
      foreach (var property in instruction)
      {
        if (property.Key.Equals("location", StringComparison.OrdinalIgnoreCase) && property.Value is JsonObject location)
        {
          RewriteSource(location, mapPath);
        }
      }
    }
  }

  private static void RewriteSource(JsonObject source, Func<string, string> mapPath)
  {
    if (source["path"] is JsonValue pathValue && pathValue.TryGetValue<string>(out var path))
    {
      source["path"] = mapPath(path);
    }

    RewriteContainedSources(source, mapPath);
  }

  private static PathMapping CreateMapping(string debuggerPrefix, string clientPrefix)
  {
    if (string.IsNullOrEmpty(debuggerPrefix))
    {
      throw new ArgumentException("sourceFileMap debugger prefixes cannot be empty.", nameof(debuggerPrefix));
    }
    if (string.IsNullOrEmpty(clientPrefix))
    {
      throw new ArgumentException("sourceFileMap client prefixes cannot be empty.", nameof(clientPrefix));
    }

    return new PathMapping(
      NormalizePrefix(debuggerPrefix),
      NormalizePrefix(clientPrefix),
      GetSeparator(debuggerPrefix),
      GetSeparator(clientPrefix));
  }

  private static string NormalizePrefix(string prefix)
  {
    var normalized = NormalizePath(prefix);
    var trimmed = normalized.TrimEnd('/');
    return trimmed.Length == 0 && normalized.StartsWith('/') ? "/" : trimmed;
  }

  private static string NormalizePath(string path) => path.Replace('\\', '/');

  private static char GetSeparator(string prefix)
  {
    var lastSlash = prefix.LastIndexOf('/');
    var lastBackslash = prefix.LastIndexOf('\\');
    if (lastBackslash > lastSlash)
    {
      return '\\';
    }
    if (lastSlash >= 0)
    {
      return '/';
    }

    return prefix.Length > 1 && prefix[1] == ':' ? '\\' : '/';
  }

  public sealed class Validator
  {
    private readonly StringComparer _prefixComparer;
    private readonly StringComparison _prefixComparison;
    private readonly List<PathMapping> _mappings = [];

    internal Validator(StringComparer prefixComparer, StringComparison prefixComparison)
    {
      _prefixComparer = prefixComparer;
      _prefixComparison = prefixComparison;
    }

    public void Add(string debuggerPrefix, string clientPrefix)
    {
      var mapping = CreateMapping(debuggerPrefix, clientPrefix);
      foreach (var existing in _mappings)
      {
        if (_prefixComparer.Equals(existing.DebuggerPrefix, mapping.DebuggerPrefix))
        {
          throw new ArgumentException(
              $"sourceFileMap contains duplicate debugger prefix '{mapping.DebuggerPrefix}'.",
              "sourceFileMap");
        }
        if (_prefixComparer.Equals(existing.ClientPrefix, mapping.ClientPrefix))
        {
          throw new ArgumentException(
              $"sourceFileMap contains ambiguous client prefix '{mapping.ClientPrefix}'.",
              "sourceFileMap");
        }

        var debuggerRelationship = GetNestingRelationship(
            existing.DebuggerPrefix,
            mapping.DebuggerPrefix,
            _prefixComparison);
        var clientRelationship = GetNestingRelationship(
            existing.ClientPrefix,
            mapping.ClientPrefix,
            _prefixComparison);
        if (debuggerRelationship != clientRelationship)
        {
          throw new ArgumentException(
              $"sourceFileMap entries '{existing.DebuggerPrefix}' and '{mapping.DebuggerPrefix}' have asymmetric client prefix nesting.",
              "sourceFileMap");
        }
      }

      _mappings.Add(mapping);
    }

    public bool AreEquivalentPrefixes(string first, string second) =>
      _prefixComparer.Equals(NormalizePrefix(first), NormalizePrefix(second));

    internal void ApplyTo(SourcePathMapper mapper)
    {
      mapper._debuggerMappings = [.. _mappings.OrderByDescending(mapping => mapping.DebuggerPrefix.Length)];
      mapper._clientMappings = [.. _mappings.OrderByDescending(mapping => mapping.ClientPrefix.Length)];
    }
  }

  private sealed record PathMapping(
    string DebuggerPrefix,
    string ClientPrefix,
    char DebuggerSeparator,
    char ClientSeparator);
}