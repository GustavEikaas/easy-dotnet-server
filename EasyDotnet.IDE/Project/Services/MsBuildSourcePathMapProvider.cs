using System.Text;
using EasyDotnet.Debugger.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Project.Services;

public sealed class MsBuildSourcePathMapProvider(ILogger<MsBuildSourcePathMapProvider> logger)
{
  public IReadOnlyDictionary<string, string> GetMappings(ProjectGraphSnapshot? snapshot)
  {
    var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
    var quarantinedDebuggerPrefixes = new List<string>();
    var quarantinedPhysicalPrefixes = new List<string>();
    var validator = new SourcePathMapper().CreateValidator();
    var evaluatedProjectCount = snapshot?.EvaluatedProjects.Count ?? 0;
    var projectsWithPathMap = 0;
    var identityEntryCount = 0;
    var skippedEntryCount = 0;
    var conflictEntryCount = 0;

    foreach (var project in snapshot?.EvaluatedProjects ?? [])
    {
      if (string.IsNullOrEmpty(project.Raw.PathMap))
      {
        continue;
      }
      projectsWithPathMap++;

      foreach (var entry in SplitWithDoubledSeparatorEscaping(project.Raw.PathMap, ','))
      {
        if (entry.Length == 0)
        {
          continue;
        }

        if (!TryParseEntry(entry, out var debuggerPrefix, out var physicalPrefix))
        {
          skippedEntryCount++;
          logger.LogWarning(
              "Skipping unsupported PathMap entry {Entry} from {ProjectPath}",
              entry,
              project.ProjectFullPath);
          continue;
        }

        var isIdentity = validator.AreEquivalentPrefixes(debuggerPrefix, physicalPrefix);
        if (isIdentity)
        {
          identityEntryCount++;

          // An omitted identity entry can still shadow an overlapping compiler mapping.
          var overlappingMappings = mappings.Where(mapping =>
              validator.AreOverlappingPrefixes(mapping.Key, debuggerPrefix)
              || validator.AreOverlappingPrefixes(mapping.Value, physicalPrefix)).ToArray();
          if (overlappingMappings.Length > 0)
          {
            foreach (var mapping in overlappingMappings)
            {
              mappings.Remove(mapping.Key);
              quarantinedDebuggerPrefixes.Add(mapping.Key);
              quarantinedPhysicalPrefixes.Add(mapping.Value);
            }
            conflictEntryCount += overlappingMappings.Length;
            LogQuarantinedEntry(entry, project.ProjectFullPath, overlappingMappings);
          }

          quarantinedDebuggerPrefixes.Add(debuggerPrefix);
          quarantinedPhysicalPrefixes.Add(physicalPrefix);
          continue;
        }

        var duplicateMapping = mappings.FirstOrDefault(mapping =>
            validator.AreEquivalentPrefixes(mapping.Key, debuggerPrefix)
            && validator.AreEquivalentPrefixes(mapping.Value, physicalPrefix));
        if (duplicateMapping.Key is not null)
        {
          skippedEntryCount++;
          continue;
        }

        var overlapsQuarantine = quarantinedDebuggerPrefixes.Any(prefix =>
            validator.AreOverlappingPrefixes(prefix, debuggerPrefix))
          || quarantinedPhysicalPrefixes.Any(prefix =>
            validator.AreOverlappingPrefixes(prefix, physicalPrefix));
        var conflicts = mappings.Where(mapping =>
            validator.AreOverlappingPrefixes(mapping.Key, debuggerPrefix)
            || validator.AreOverlappingPrefixes(mapping.Value, physicalPrefix)).ToArray();
        if (overlapsQuarantine || conflicts.Length > 0)
        {
          foreach (var mapping in conflicts)
          {
            mappings.Remove(mapping.Key);
            quarantinedDebuggerPrefixes.Add(mapping.Key);
            quarantinedPhysicalPrefixes.Add(mapping.Value);
          }
          quarantinedDebuggerPrefixes.Add(debuggerPrefix);
          quarantinedPhysicalPrefixes.Add(physicalPrefix);
          conflictEntryCount += conflicts.Length + 1;
          if (conflicts.Length > 0)
          {
            LogQuarantinedEntry(entry, project.ProjectFullPath, conflicts);
          }
          else
          {
            logger.LogWarning(
                "Skipping quarantined PathMap entry {Entry} from {ProjectPath}; debugger or physical prefix overlaps a conflicting region",
                entry,
                project.ProjectFullPath);
          }
          continue;
        }

        mappings.Add(debuggerPrefix, physicalPrefix);
      }
    }

    logger.LogInformation(
        "Automatic MSBuild PathMap discovery: {EvaluatedProjectCount} evaluated projects, {ProjectsWithPathMap} with nonempty PathMap, {AcceptedMappingCount} accepted mappings, {IdentityEntryCount} identity entries omitted, {SkippedEntryCount} entries skipped, {ConflictEntryCount} conflicts skipped",
        evaluatedProjectCount,
        projectsWithPathMap,
        mappings.Count,
        identityEntryCount,
        skippedEntryCount,
        conflictEntryCount);

    return mappings;

    void LogQuarantinedEntry(
        string entry,
        string projectPath,
        IReadOnlyCollection<KeyValuePair<string, string>> conflicts)
      => logger.LogWarning(
          "Quarantining PathMap entry {Entry} from {ProjectPath} with accepted mappings {AcceptedMappings}; debugger or physical prefix regions overlap",
          entry,
          projectPath,
          string.Join(", ", conflicts.Select(mapping => $"{mapping.Value}={mapping.Key}")));
  }

  private static bool TryParseEntry(
      string entry,
      out string debuggerPrefix,
      out string physicalPrefix)
  {
    debuggerPrefix = "";
    physicalPrefix = "";

    var parts = SplitWithDoubledSeparatorEscaping(entry, '=');
    if (parts.Length != 2)
    {
      return false;
    }

    physicalPrefix = parts[0];
    debuggerPrefix = parts[1];
    return physicalPrefix.Length > 0 && debuggerPrefix.Length > 0;
  }

  private static string[] SplitWithDoubledSeparatorEscaping(string value, char separator)
  {
    if (value.Length == 0)
    {
      return [];
    }

    var result = new List<string>();
    var part = new StringBuilder();
    var index = 0;
    while (index < value.Length)
    {
      var character = value[index++];
      if (character == separator)
      {
        if (index < value.Length && value[index] == separator)
        {
          index++;
        }
        else
        {
          result.Add(part.ToString());
          part.Clear();
          continue;
        }
      }

      part.Append(character);
    }

    result.Add(part.ToString());
    return [.. result];
  }
}