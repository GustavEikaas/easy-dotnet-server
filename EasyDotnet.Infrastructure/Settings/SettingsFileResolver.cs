using System.Text;
using System.Security.Cryptography;

namespace EasyDotnet.Infrastructure.Settings;

public enum SettingsScope
{
  Solution,
  Project
}

/// <summary>
/// Resolves file paths for settings storage based on solution/project paths
/// </summary>
public class SettingsFileResolver
{
  private readonly string _settingsDirectory;

  public SettingsFileResolver(string? settingsDirectory = null)
  {
    _settingsDirectory = settingsDirectory ?? GetDefaultSettingsDirectory();
    EnsureDirectoryExists();
  }

  /// <summary>
  /// Gets the full file path for storing settings for the given solution/project
  /// </summary>
  public string GetSettingsFilePath(string sourcePath, SettingsScope scope)
  {
    var hash = ComputeHash(sourcePath);
    var prefix = scope == SettingsScope.Solution ? "solution" : "project";
    var fileName = $"{prefix}_{hash}.json";
    return Path.Combine(_settingsDirectory, fileName);
  }

  /// <summary>
  /// Gets all settings files for a given scope
  /// </summary>
  public IEnumerable<string> GetAllSettingsFiles(SettingsScope scope)
  {
    var prefix = scope == SettingsScope.Solution ? "solution" : "project";
    var pattern = $"{prefix}_*.json";
    return Directory.Exists(_settingsDirectory)
        ? Directory.GetFiles(_settingsDirectory, pattern)
        : Enumerable.Empty<string>();
  }

  /// <summary>
  /// Computes MD5 hash of the full path for deterministic file naming
  /// </summary>
  private static string ComputeHash(string path)
  {
    var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
    var bytes = Encoding.UTF8.GetBytes(normalizedPath);
    var hash = MD5.HashData(bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  private void EnsureDirectoryExists()
  {
    if (!Directory.Exists(_settingsDirectory))
    {
      Directory.CreateDirectory(_settingsDirectory);
    }
  }

  private static string GetDefaultSettingsDirectory()
  {
    var dataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(dataPath, "easy-dotnet");
  }
}