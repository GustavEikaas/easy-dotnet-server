using System.Text;
using System.Security.Cryptography;
using System.IO.Abstractions;

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
  private readonly IFileSystem _fileSystem;

  public SettingsFileResolver(IFileSystem fileSystem, string? settingsDirectory = null)
  {
    _fileSystem = fileSystem ?? new FileSystem();
    _settingsDirectory = settingsDirectory ?? GetDefaultSettingsDirectory();
    EnsureDirectoryExists();
  }

  /// <summary>
  /// Gets the full file path for storing settings for the given solution/project
  /// </summary>
  public string GetSettingsFilePath(string sourcePath, SettingsScope scope)
  {
    if (!_fileSystem.File.Exists(sourcePath))
    {
      throw new FileNotFoundException("The provided path does not point to a valid file.", sourcePath);
    }

    var hash = ComputeHash(sourcePath);
    var prefix = scope == SettingsScope.Solution ? "solution" : "project";
    var fileName = $"{prefix}_{hash}.json";
    return _fileSystem.Path.Combine(_settingsDirectory, fileName);
  }

  /// <summary>
  /// Gets all settings files for a given scope
  /// </summary>
  public IEnumerable<string> GetAllSettingsFiles(SettingsScope scope)
  {
    var prefix = scope == SettingsScope.Solution ? "solution" : "project";
    var pattern = $"{prefix}_*.json";
    return _fileSystem.Directory.Exists(_settingsDirectory)
        ? _fileSystem.Directory.GetFiles(_settingsDirectory, pattern)
        : [];
  }

  /// <summary>
  /// Computes MD5 hash of the full path for deterministic file naming
  /// </summary>
  private string ComputeHash(string path)
  {
    var normalizedPath = _fileSystem.Path.GetFullPath(path).ToLowerInvariant();
    var bytes = Encoding.UTF8.GetBytes(normalizedPath);
    var hash = MD5.HashData(bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  private void EnsureDirectoryExists()
  {
    if (!_fileSystem.Directory.Exists(_settingsDirectory))
    {
      _fileSystem.Directory.CreateDirectory(_settingsDirectory);
    }
  }

  private string GetDefaultSettingsDirectory()
  {
    var dataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return _fileSystem.Path.Combine(dataPath, "easy-dotnet");
  }
}