using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EasyDotnet.Controllers.LaunchProfile;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Services;

public class LaunchProfileService(ILogger<LaunchProfileService> logger)
{

  private static readonly JsonSerializerOptions DeserializerOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public Dictionary<string, LaunchProfile> GetLaunchProfiles(string targetPath)
  {

    var launchSettingsPath = Path.Combine(
        targetPath,
        "Properties",
        "launchSettings.json"
    );

    if (!File.Exists(launchSettingsPath))
    {
      logger.LogInformation("File not found {file}", launchSettingsPath);
      return [];
    }

    var json = File.ReadAllText(launchSettingsPath) ?? throw new Exception($"Failed to read file {launchSettingsPath}");

    logger.LogInformation("launch profiles  {json}", json);
    var settings = JsonSerializer.Deserialize<LaunchSettings>(json, DeserializerOptions);
    return settings?.Profiles ?? [];
  }
}
