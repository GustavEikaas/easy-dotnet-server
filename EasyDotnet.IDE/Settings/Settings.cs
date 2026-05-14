namespace EasyDotnet.IDE.Settings;

/// <summary>
/// Base interface for all versioned settings
/// </summary>
public interface IVersionedSettings
{
  int Version { get; set; }
  SettingsMetadata Metadata { get; set; }
}

/// <summary>
/// Metadata tracked for all settings files
/// </summary>
public class SettingsMetadata
{
  public required string OriginalPath { get; set; }
  public DateTime LastAccessed { get; set; }
}

/// <summary>
/// Settings stored per solution file
/// </summary>
public class SolutionSettings : IVersionedSettings
{
  public int Version { get; set; } = 1;
  public required SettingsMetadata Metadata { get; set; }
  public DefaultProjects? Defaults { get; set; }
  public SolutionBuildConfigurationSettings? BuildConfiguration { get; set; }
}

/// <summary>
/// Default project selections for various operations
/// </summary>
public class DefaultProjects
{
  public string? BuildProject { get; set; }
  public string? StartupProject { get; set; }
  public string? TestProject { get; set; }
  public string? ViewProject { get; set; }
}

/// <summary>
/// Settings stored per project file
/// </summary>
public class ProjectSettings : IVersionedSettings
{
  public int Version { get; set; } = 1;
  public required SettingsMetadata Metadata { get; set; }
  //Should be null unless its a multi tfm project
  public string? TargetFramework { get; set; }
  public string? RunSettings { get; set; }
  public string? LaunchProfile { get; set; }
}

public class SolutionBuildConfigurationSettings
{
  public string? ActiveBuildType { get; set; }
  public string? ActivePlatform { get; set; }
  public string[]? KnownBuildTypes { get; set; }
  public string[]? KnownPlatforms { get; set; }
}