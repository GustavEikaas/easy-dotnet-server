using System.IO.Abstractions;
using EasyDotnet.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Settings;


/// <summary>
/// Main service for managing IDE settings
/// </summary>
public class SettingsService(
    IFileSystem fileSystem,
    SettingsFileResolver fileResolver,
    SettingsSerializer serializer,
    IClientService clientService,
    ILaunchProfileService launchProfileService,
    IBuildHostManager buildHostManager,
    ILogger<SettingsService> logger)
{
  #region Solution Settings

  public string? GetDefaultBuildProject(string solutionPath)
  {
    var settings = GetOrCreateSolutionSettings(solutionPath);
    return settings?.Defaults?.BuildProject;
  }

  public void SetDefaultBuildProject(string? projectName)
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    if (sln is null) return;

    var settings = GetOrCreateSolutionSettings(sln);
    settings.Defaults ??= new DefaultProjects();

    settings.Defaults.BuildProject = projectName;
    SaveSolutionSettings(sln, settings);
  }

  public void SetDefaultTestProject(string? projectName)
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    if (sln is null) return;

    var settings = GetOrCreateSolutionSettings(sln);
    settings.Defaults ??= new DefaultProjects();

    settings.Defaults.TestProject = projectName;
    SaveSolutionSettings(sln, settings);
  }

  public string? GetDefaultTestProject()
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    if (sln is null) return null;

    var settings = GetOrCreateSolutionSettings(sln);
    return settings?.Defaults?.TestProject;
  }

  public void SetDefaultStartupProject(string? projectName)
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    if (sln is null) return;

    var settings = GetOrCreateSolutionSettings(sln);
    settings.Defaults ??= new DefaultProjects();

    settings.Defaults.StartupProject = projectName;
    SaveSolutionSettings(sln, settings);
  }

  public string? GetDefaultStartupProject()
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    if (sln is null) return null;

    var settings = GetOrCreateSolutionSettings(sln);
    return settings?.Defaults?.StartupProject;
  }

  public string? GetDefaultViewProject()
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    if (sln is null) return null;

    var settings = GetOrCreateSolutionSettings(sln);
    return settings?.Defaults?.ViewProject;
  }

  public void SetDefaultViewProject(string? projectName)
  {

    var sln = clientService.ProjectInfo?.SolutionFile;
    if (sln is null) return;

    var settings = GetOrCreateSolutionSettings(sln);
    settings.Defaults ??= new DefaultProjects();

    settings.Defaults.ViewProject = projectName;
    SaveSolutionSettings(sln, settings);
  }

  #endregion

  #region Project Settings

  public async Task<string?> GetProjectTargetFramework(string projectPath, CancellationToken cancellationToken)
  {
    if (!ValidateProjectExists(projectPath))
      return null;

    var settings = await GetValidatedProjectSettings(projectPath, cancellationToken);
    return settings?.TargetFramework;
  }

  public void SetProjectTargetFramework(string projectPath, string? tfm)
  {
    var settings = GetOrCreateProjectSettings(projectPath);
    settings.TargetFramework = tfm;
    SaveProjectSettings(projectPath, settings);
  }

  public string? GetProjectRunSettings(string projectPath)
  {
    if (!ValidateProjectExists(projectPath))
      return null;

    var settings = GetOrCreateProjectSettings(projectPath);
    return settings?.RunSettings;
  }

  public void SetProjectRunSettings(string projectPath, string? runSettings)
  {
    var settings = GetOrCreateProjectSettings(projectPath);
    settings.RunSettings = runSettings;
    SaveProjectSettings(projectPath, settings);
  }

  public async Task<string?> GetProjectLaunchProfile(string projectPath, CancellationToken cancellationToken)
  {
    if (!ValidateProjectExists(projectPath))
      return null;

    var settings = await GetValidatedProjectSettings(projectPath, cancellationToken);
    return settings?.LaunchProfile;
  }

  public void SetProjectLaunchProfile(string projectPath, string? launchProfile)
  {
    var settings = GetOrCreateProjectSettings(projectPath);
    settings.LaunchProfile = launchProfile;
    SaveProjectSettings(projectPath, settings);
  }

  public async Task<ProjectSettings?> GetValidatedProjectSettings(string projectPath, CancellationToken cancellationToken)
  {
    if (!ValidateProjectExists(projectPath))
    {
      return null;
    }

    await ValidateTargetFrameworkAsync(projectPath, cancellationToken);
    ValidateLaunchProfile(projectPath);

    return GetOrCreateProjectSettings(projectPath);
  }

  private async Task ValidateTargetFrameworkAsync(string projectPath, CancellationToken cancellationToken)
  {
    var settings = GetOrCreateProjectSettings(projectPath);
    if (settings.TargetFramework is null)
    {
      return;
    }

    var projectTfms = await buildHostManager
      .GetProjectPropertiesBatchAsync(new([projectPath], null), cancellationToken)
      .ToListAsync(cancellationToken);

    if (projectTfms.Count <= 1 || !projectTfms.Any(x => x.TargetFramework == settings.TargetFramework))
    {
      settings.TargetFramework = null;
      SaveProjectSettings(projectPath, settings);
    }
  }

  private void ValidateLaunchProfile(string projectPath)
  {
    var settings = GetOrCreateProjectSettings(projectPath);
    if (settings.LaunchProfile is null)
    {
      return;
    }

    var profiles = launchProfileService.GetLaunchProfiles(projectPath);

    if (profiles?.ContainsKey(settings.LaunchProfile) != true)
    {
      settings.LaunchProfile = null;
      SaveProjectSettings(projectPath, settings);
    }
  }

  #endregion

  #region Private Helpers

  private SolutionSettings GetOrCreateSolutionSettings(string solutionPath)
  {
    var filePath = fileResolver.GetSettingsFilePath(solutionPath, SettingsScope.Solution);
    var settings = serializer.Read<SolutionSettings>(filePath);

    return settings ?? new SolutionSettings
    {
      Metadata = new SettingsMetadata
      {
        OriginalPath = Path.GetFullPath(solutionPath),
        LastAccessed = DateTime.UtcNow
      }
    };
  }

  private void SaveSolutionSettings(string solutionPath, SolutionSettings settings)
  {
    var filePath = fileResolver.GetSettingsFilePath(solutionPath, SettingsScope.Solution);
    settings.Metadata.LastAccessed = DateTime.UtcNow;
    serializer.Write(filePath, settings);
  }

  private ProjectSettings GetOrCreateProjectSettings(string projectPath)
  {
    var filePath = fileResolver.GetSettingsFilePath(projectPath, SettingsScope.Project);
    var settings = serializer.Read<ProjectSettings>(filePath);

    return settings ?? new ProjectSettings
    {
      Metadata = new SettingsMetadata
      {
        OriginalPath = Path.GetFullPath(projectPath),
        LastAccessed = DateTime.UtcNow
      }
    };
  }

  private void SaveProjectSettings(string projectPath, ProjectSettings settings)
  {
    var filePath = fileResolver.GetSettingsFilePath(projectPath, SettingsScope.Project);
    settings.Metadata.LastAccessed = DateTime.UtcNow;
    serializer.Write(filePath, settings);
  }

  private bool ValidateProjectExists(string projectPath)
  {
    if (fileSystem.File.Exists(projectPath))
      return true;

    logger.LogWarning("Project file not found, deleting settings: {ProjectPath}", projectPath);
    var filePath = fileResolver.GetSettingsFilePath(projectPath, SettingsScope.Project);
    serializer.Delete(filePath);
    return false;
  }

  #endregion
}