using EasyDotnet.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Settings;


/// <summary>
/// Main service for managing IDE settings
/// </summary>
public class SettingsService(
    SettingsFileResolver fileResolver,
    SettingsSerializer serializer,
    IClientService clientService,
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

  public string? GetDefaultDebugProject()
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    if (sln is null) return null;

    var settings = GetOrCreateSolutionSettings(sln);
    return settings?.Defaults?.DebugProject;
  }

  public void SetDefaultDebugProject(string? projectName)
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    if (sln is null) return;

    var settings = GetOrCreateSolutionSettings(sln);
    settings.Defaults ??= new DefaultProjects();

    settings.Defaults.DebugProject = projectName;
    SaveSolutionSettings(sln, settings);
  }

  public string? GetDefaultRunProject()
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    if (sln is null) return null;

    var settings = GetOrCreateSolutionSettings(sln);
    return settings?.Defaults?.RunProject;
  }

  public void SetDefaultRunProject(string? projectName)
  {
    var sln = clientService.ProjectInfo?.SolutionFile;
    if (sln is null) return;

    var settings = GetOrCreateSolutionSettings(sln);
    settings.Defaults ??= new DefaultProjects();

    settings.Defaults.RunProject = projectName;
    SaveSolutionSettings(sln, settings);
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

  public string? GetProjectTargetFramework(string projectPath)
  {
    if (!ValidateProjectExists(projectPath))
      return null;

    var settings = GetOrCreateProjectSettings(projectPath);
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

  public string? GetProjectLaunchProfile(string projectPath)
  {
    if (!ValidateProjectExists(projectPath))
      return null;

    var settings = GetOrCreateProjectSettings(projectPath);
    return settings?.LaunchProfile;
  }

  public void SetProjectLaunchProfile(string projectPath, string? launchProfile)
  {
    var settings = GetOrCreateProjectSettings(projectPath);
    settings.LaunchProfile = launchProfile;
    SaveProjectSettings(projectPath, settings);
  }

  public ProjectSettings? GetProjectSettings(string projectPath)
  {
    if (!ValidateProjectExists(projectPath))
      return null;

    return GetOrCreateProjectSettings(projectPath);
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
    if (File.Exists(projectPath))
      return true;

    logger.LogWarning("Project file not found, deleting settings: {ProjectPath}", projectPath);
    var filePath = fileResolver.GetSettingsFilePath(projectPath, SettingsScope.Project);
    serializer.Delete(filePath);
    return false;
  }

  #endregion
}