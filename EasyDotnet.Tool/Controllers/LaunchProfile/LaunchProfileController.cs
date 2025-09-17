using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.LaunchProfile;

public sealed record LaunchProfileResponse(string Name, LaunchProfile Value);

public class LaunchProfileController(ILogger<LaunchProfileController> logger) : BaseController
{
  private static readonly JsonSerializerOptions DeserializerOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  [JsonRpcMethod("launch-profiles")]
  public IAsyncEnumerable<LaunchProfileResponse> GetLaunchProfiles(string targetPath)
  {
    var launchSettingsPath = Path.Combine(
        targetPath,
        "Properties",
        "launchSettings.json"
    );

    if (!File.Exists(launchSettingsPath))
    {
      logger.LogInformation("File not found {file}", launchSettingsPath);
      return Enumerable.Empty<LaunchProfileResponse>().AsAsyncEnumerable();
    }

    var json = File.ReadAllText(launchSettingsPath) ?? throw new Exception($"Failed to read file {launchSettingsPath}");

    logger.LogInformation("launch profiles  {json}", json);
    var settings = JsonSerializer.Deserialize<LaunchSettings>(json, DeserializerOptions);

    return (settings?.Profiles.Select(x => new LaunchProfileResponse(x.Key, x.Value)) ?? []).AsAsyncEnumerable();
  }
}