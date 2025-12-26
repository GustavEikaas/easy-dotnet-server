using Microsoft.Extensions.Logging;
namespace EasyDotnet.Infrastructure.Settings;

/// <summary>
/// Cleans up orphaned settings files whose original source files no longer exist
/// </summary>
public class SettingsGarbageCollector(
    SettingsFileResolver fileResolver,
    SettingsSerializer serializer,
    ILogger<SettingsGarbageCollector> logger)
{

  /// <summary>
  /// Runs cleanup of orphaned settings files as a background task
  /// </summary>
  public Task RunCleanupAsync(CancellationToken cancellationToken = default) => Task.Run(() => RunCleanup(cancellationToken), cancellationToken);

  /// <summary>
  /// Performs the actual cleanup operation
  /// </summary>
  private void RunCleanup(CancellationToken cancellationToken)
  {
    logger.LogInformation("Starting settings garbage collection");

    try
    {
      var deletedCount = 0;

      deletedCount += CleanupScope(SettingsScope.Solution, cancellationToken);
      deletedCount += CleanupScope(SettingsScope.Project, cancellationToken);

      logger.LogInformation("Settings garbage collection completed. Deleted {Count} orphaned files", deletedCount);
    }
    catch (OperationCanceledException)
    {
      logger.LogInformation("Settings garbage collection was cancelled");
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error during settings garbage collection");
    }
  }

  private int CleanupScope(SettingsScope scope, CancellationToken cancellationToken)
  {
    var deletedCount = 0;
    foreach (var filePath in fileResolver.GetAllSettingsFiles(scope))
    {
      cancellationToken.ThrowIfCancellationRequested();

      try
      {
        if (ShouldDeleteFile(filePath, scope))
        {
          serializer.Delete(filePath);
          deletedCount++;
        }
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Failed to process settings file: {FilePath}", filePath);
      }
    }

    return deletedCount;
  }

  private bool ShouldDeleteFile(string settingsFilePath, SettingsScope scope)
  {
    try
    {
      // Read the settings to get the original path
      IVersionedSettings? settings = scope == SettingsScope.Solution
          ? serializer.Read<SolutionSettings>(settingsFilePath)
          : serializer.Read<ProjectSettings>(settingsFilePath);

      if (settings == null)
      {
        // File couldn't be read (corrupted or doesn't exist)
        // Serializer already deleted it if corrupted
        return false;
      }

      var originalPath = settings.Metadata.OriginalPath;

      // Check if the original file still exists
      if (!File.Exists(originalPath))
      {
        logger.LogInformation(
            "Original file no longer exists, marking for deletion: {OriginalPath}",
            originalPath);
        return true;
      }

      return false;
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Error checking if file should be deleted: {FilePath}", settingsFilePath);
      return false;
    }
  }
}