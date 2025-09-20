using System.Collections.Generic;
using System.Text.Json;

namespace EasyDotnet.Controllers.LaunchProfile;

public sealed record LaunchSettings
{
  public Dictionary<string, LaunchProfile> Profiles { get; init; } = [];
}

public sealed record LaunchProfile
{
  public string? CommandName { get; init; }
  public bool? DotnetRunMessages { get; init; }
  public bool? LaunchBrowser { get; init; }
  public string? ApplicationUrl { get; init; }
  public Dictionary<string, string> EnvironmentVariables { get; init; } = [];
  public string? CommandLineArgs { get; init; }
  public string? WorkingDirectory { get; init; }
  public Dictionary<string, JsonElement> Other { get; init; } = [];
}