using System.Text.Json.Serialization;

namespace EasyDotnet.Aspire.Models;

public record EnvVar(string Name, string Value);

public class RunSessionPayload
{
  [JsonPropertyName("launch_configurations")]
  public LaunchConfigurationDto[] LaunchConfigurations { get; set; } = [];

  [JsonPropertyName("env")]
  public EnvVar[]? Env { get; set; }

  [JsonPropertyName("args")]
  public string[]? Args { get; set; }
}

public class LaunchConfigurationDto
{
  public string? Type { get; set; }
  public string? Mode { get; set; }
  public string? ProjectPath { get; set; }
  public string? LaunchProfile { get; set; }
  public bool? DisableLaunchProfile { get; set; }
}