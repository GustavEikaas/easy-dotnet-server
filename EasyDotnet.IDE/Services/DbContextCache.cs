using System.Text.Json;
using EasyDotnet.Infrastructure.EntityFramework;

namespace EasyDotnet.IDE.Services;

public class DbContextCache(IAppPathsService appPathsService)
{
  private readonly string _cacheFilePath = Path.Combine(appPathsService.CacheDirectory, "ef_context_cache.json");
  private readonly JsonSerializerOptions _jsonSerializerOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  public async Task<List<DbContextInfo>?> TryGetAsync(string efProj, string startupProj)
  {
    if (!File.Exists(_cacheFilePath)) return null;

    try
    {
      var json = await File.ReadAllTextAsync(_cacheFilePath);
      var cache = JsonSerializer.Deserialize<Dictionary<string, List<DbContextInfo>>>(json, _jsonSerializerOptions);
      var key = GetKey(efProj, startupProj);

      return cache != null && cache.TryGetValue(key, out var list) ? list : null;
    }
    catch
    {
      return null;
    }
  }

  public async Task SetAsync(string efProj, string startupProj, List<DbContextInfo> contexts)
  {
    Dictionary<string, List<DbContextInfo>> cache;
    try
    {
      if (File.Exists(_cacheFilePath))
      {
        var json = await File.ReadAllTextAsync(_cacheFilePath);
        cache = JsonSerializer.Deserialize<Dictionary<string, List<DbContextInfo>>>(json, _jsonSerializerOptions) ?? [];
      }
      else
      {
        cache = [];
      }
    }
    catch
    {
      cache = [];
    }

    cache[GetKey(efProj, startupProj)] = contexts;

    await File.WriteAllTextAsync(_cacheFilePath, JsonSerializer.Serialize(cache, _jsonSerializerOptions));
  }

  private static string GetKey(string ef, string startup) => $"{ef}::{startup}";
}