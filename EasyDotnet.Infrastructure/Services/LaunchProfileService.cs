using System.Text.Json;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.LaunchProfile;

namespace EasyDotnet.Infrastructure.Services;

public class LaunchProfileService : ILaunchProfileService
{
  private static readonly JsonSerializerOptions DeserializerOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public Dictionary<string, LaunchProfile>? GetLaunchProfiles(string targetPath)
  {
    var launchSettingsPath = Path.Combine(
        targetPath,
        "Properties",
        "launchSettings.json"
    );

    if (!File.Exists(launchSettingsPath))
    {
      return null;
    }

    var json = File.ReadAllText(launchSettingsPath) ?? throw new Exception($"Failed to read file {launchSettingsPath}");

    var settings = JsonSerializer.Deserialize<LaunchSettings>(json, DeserializerOptions);
    return settings?.Profiles ?? [];
  }
}