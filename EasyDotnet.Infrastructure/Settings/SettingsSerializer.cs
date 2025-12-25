using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Settings;

/// <summary>
/// Handles serialization and deserialization of settings files
/// </summary>
public class SettingsSerializer
{
  private readonly ILogger<SettingsSerializer> _logger;
  private readonly JsonSerializerOptions _jsonOptions;

  public SettingsSerializer(ILogger<SettingsSerializer> logger)
  {
    _logger = logger;
    _jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
  }

  /// <summary>
  /// Reads settings from file. Returns null if file doesn't exist or is corrupted.
  /// Deletes corrupted files automatically.
  /// </summary>
  public T? Read<T>(string filePath) where T : IVersionedSettings
  {
    if (!File.Exists(filePath))
    {
      return default;
    }

    try
    {
      var json = File.ReadAllText(filePath);
      var settings = JsonSerializer.Deserialize<T>(json, _jsonOptions);

      if (settings == null)
      {
        _logger.LogWarning("Settings file deserialized to null: {FilePath}", filePath);
        DeleteCorruptedFile(filePath);
        return default;
      }

      // Update last accessed time
      settings.Metadata.LastAccessed = DateTime.UtcNow;
      Write(filePath, settings);

      return settings;
    }
    catch (JsonException ex)
    {
      _logger.LogWarning(ex, "Corrupted settings file detected: {FilePath}", filePath);
      DeleteCorruptedFile(filePath);
      return default;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to read settings file: {FilePath}", filePath);
      return default;
    }
  }

  /// <summary>
  /// Writes settings to file. Creates file if it doesn't exist.
  /// </summary>
  public void Write<T>(string filePath, T settings) where T : IVersionedSettings
  {
    try
    {
      var json = JsonSerializer.Serialize(settings, _jsonOptions);
      File.WriteAllText(filePath, json);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to write settings file: {FilePath}", filePath);
      throw;
    }
  }

  /// <summary>
  /// Deletes a settings file
  /// </summary>
  public void Delete(string filePath)
  {
    try
    {
      if (File.Exists(filePath))
      {
        File.Delete(filePath);
        _logger.LogInformation("Deleted settings file: {FilePath}", filePath);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to delete settings file: {FilePath}", filePath);
    }
  }

  private void DeleteCorruptedFile(string filePath)
  {
    try
    {
      File.Delete(filePath);
      _logger.LogInformation("Deleted corrupted settings file: {FilePath}", filePath);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to delete corrupted file: {FilePath}", filePath);
    }
  }
}
