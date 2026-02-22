using System.Text.Json;
using EasyDotnet.Application.Interfaces;

namespace EasyDotnet.IDE.Utils;

public class GlobalJsonService(IClientService clientService)
{
  private readonly JsonSerializerOptions _jsonSerializerOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
  };

  public GlobalJson? GetGlobalJson()
  {
    var rootDir = clientService.RequireRootDir();
    var directory = new DirectoryInfo(rootDir);

    while (directory != null)
    {
      var filePath = Path.Combine(directory.FullName, "global.json");

      if (File.Exists(filePath))
      {
        try
        {
          var jsonContent = File.ReadAllText(filePath);
          return JsonSerializer.Deserialize<GlobalJson>(jsonContent, _jsonSerializerOptions);
        }
        catch (JsonException)
        {
          return null;
        }
      }
      directory = directory.Parent;
    }

    return null;
  }
}

public record TestOptions(string? Runner);

public record GlobalJson(GlobalJsonSdk? Sdk, Dictionary<string, string>? MsbuildSdks, TestOptions? Test);

public record GlobalJsonSdk(string? Version, bool? AllowPrerelease, string? RollForward);

public static class GlobalJsonExtensions
{
  public static bool IsMicrosoftTestingPlatformRunner(this GlobalJson? globalJson) => globalJson?.Test?.Runner == "Microsoft.Testing.Platform";
}