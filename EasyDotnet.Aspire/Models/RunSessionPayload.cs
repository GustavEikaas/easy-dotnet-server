using System.Text.Json.Serialization;

namespace EasyDotnet.Aspire.Models;

public record EnvVar(string Name, string Value);

public class RunSessionPayload
{
  [JsonPropertyName("launch_configurations")]
  public LaunchConfiguration[] LaunchConfigurations { get; set; } = [];

  [JsonPropertyName("env")]
  public EnvVar[]? Env { get; set; }

  [JsonPropertyName("args")]
  public string[]? Args { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ProjectLaunchConfiguration), "project")]
public class LaunchConfiguration
{
  [JsonPropertyName("mode")]
  public string? Mode { get; set; } // "Debug" or "NoDebug"
}

public class ProjectLaunchConfiguration : LaunchConfiguration
{
  [JsonPropertyName("project_path")]
  public string ProjectPath { get; set; } = string.Empty;

  [JsonPropertyName("launch_profile")]
  public string? LaunchProfile { get; set; }

  [JsonPropertyName("disable_launch_profile")]
  public bool? DisableLaunchProfile { get; set; }
}