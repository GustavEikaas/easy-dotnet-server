using System.Text.Json;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.LaunchProfile;

namespace EasyDotnet.Infrastructure.Services;

public class LaunchProfileService : ILaunchProfileService
{
  private static readonly JsonSerializerOptions DeserializerOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip
  };

  public LaunchProfile? GetLaunchProfile(string targetPath, string? profileName)
  {
    if (string.IsNullOrEmpty(profileName))
    {
      return null;
    }

    var profiles = GetLaunchProfiles(targetPath);

    if (profiles != null && profiles.TryGetValue(profileName, out var profile))
    {
      return profile;
    }

    return null;
  }

  public Dictionary<string, LaunchProfile>? GetLaunchProfiles(string targetPath)
  {
    var baseDir = File.Exists(targetPath) ? Path.GetDirectoryName(targetPath)! : targetPath;

    var launchSettingsPath = Path.Combine(baseDir, "Properties", "launchSettings.json");

    if (!File.Exists(launchSettingsPath))
    {
      return null;
    }

    var json = File.ReadAllText(launchSettingsPath) ?? throw new Exception($"Failed to read file {launchSettingsPath}");

    var settings = JsonSerializer.Deserialize<LaunchSettings>(json, DeserializerOptions);
    return settings?.Profiles?.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value) ?? [];
  }
}