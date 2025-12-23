using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.Domain.Models.IDE;
using EasyDotnet.IDE.Extensions;
using EasyDotnet.Infrastructure.Services;
using EasyDotnet.MsBuild;

namespace EasyDotnet.IDE.Services;

public class WorkspaceService(ClientService clientService, ISolutionService solutionService, IMsBuildService msBuildService, WorkspaceSettingsStore workspaceSettingsStore)
{
  public async Task<string?> PickBuildProject(bool useDefault)
  {
    if (string.IsNullOrEmpty(clientService.ProjectInfo?.RootDir))
    {
      throw new InvalidOperationException("No root dir");
    }

    if (useDefault)
    {
      var defaultProject = workspaceSettingsStore.GetDefaultBuildProject();
      if (defaultProject != null)
      {
        return defaultProject.Type == WorkspaceProjectType.Solution
          ? clientService.ProjectInfo.SolutionFile
          : defaultProject.Project;
      }
    }

    var projects = GetProjectPaths();

    if (projects.Length == 0)
    {
      throw new InvalidOperationException("No buildable projects found");
    }

    if (projects.Length == 1)
    {
      return projects[0];
    }

    var buildableTargets = !string.IsNullOrEmpty(clientService.ProjectInfo.SolutionFile)
      && File.Exists(clientService.ProjectInfo.SolutionFile)
      ? [.. projects.Prepend(clientService.ProjectInfo.SolutionFile)]
      : projects;

    var (loadedProjects, unloadedProjects) = await msBuildService.LoadProjects(
      projects,
      TimeSpan.FromSeconds(0));

    var choices = buildableTargets.Select(path =>
    {
      var ext = Path.GetExtension(path);

      if (ext.Equals(FileTypes.SolutionExtension, StringComparison.OrdinalIgnoreCase) ||
          ext.Equals(FileTypes.SolutionXExtension, StringComparison.OrdinalIgnoreCase))
      {
        return path.FromSolutionFile();
      }

      var loadedProject = loadedProjects.FirstOrDefault(p =>
        p.MSBuildProjectFullPath?.Equals(path, StringComparison.OrdinalIgnoreCase) == true);

      return loadedProject != null
        ? loadedProject.FromDotnetProject()
        : path.FromProjectPath();
    }).ToArray();

    var res = await clientService.RequestSelection("Select project to build", choices, null);

    return res?.Id;
  }

  public async Task<WorkspaceProjectReference?> PickRunProject(bool useDefault)
  {
    if (useDefault)
    {
      var def = workspaceSettingsStore.GetDefaultRunProject();
      if (def != null) return def;
    }

    var paths = GetProjectPaths();
    if (paths.Length == 0) throw new InvalidOperationException("No projects found");

    var (loadedProjects, unloadedPaths) = await msBuildService.LoadProjects(paths, TimeSpan.FromSeconds(2));

    var runnableChoices = loadedProjects
        .Where(p => p.IsRunnable())
        .Select(p => p.FromDotnetProject());

    var unknownChoices = unloadedPaths
        .Select(path => path.FromProjectPath());

    var allChoices = runnableChoices.Concat(unknownChoices).ToArray();

    if (allChoices.Length == 0)
    {
      throw new InvalidOperationException("No runnable projects found.");
    }

    string selectedPath;
    if (allChoices.Length == 1)
    {
      selectedPath = allChoices[0].Id;
    }
    else
    {
      var selection = await clientService.RequestSelection("Select project to run", allChoices);
      if (selection == null) return null;
      selectedPath = selection.Id;
    }

    var projectProps = loadedProjects.FirstOrDefault(p => p.MSBuildProjectFullPath == selectedPath)
                       ?? await msBuildService.GetOrSetProjectPropertiesAsync(selectedPath);

    if (!projectProps.IsRunnable())
    {
      throw new InvalidOperationException($"The project '{projectProps.ProjectName}' is not executable (OutputType: {projectProps.OutputType}).");
    }

    return await ResolveTfmForProject(projectProps, WorkspaceProjectType.Project);
  }

  private async Task<WorkspaceProjectReference?> ResolveTfmForProject(DotnetProject project, WorkspaceProjectType type)
  {
    var path = project.MSBuildProjectFullPath!;

    if (project.TargetFrameworks is { Length: > 1 })
    {
      var tfmChoices = project.TargetFrameworks
          .Select(t => new SelectionOption(t, t))
          .ToArray();

      var selectedTfm = await clientService.RequestSelection(
          $"Select Target Framework for {Path.GetFileNameWithoutExtension(path)}",
          tfmChoices);

      if (selectedTfm == null) return null;

      return new WorkspaceProjectReference(type, path, selectedTfm.Id);
    }

    var singleTfm = project.TargetFrameworks?.FirstOrDefault() ?? project.TargetFramework;
    return new WorkspaceProjectReference(type, path, singleTfm);
  }

  private string[] GetProjectPaths()
  {
    if (string.IsNullOrEmpty(clientService.ProjectInfo?.RootDir))
      throw new InvalidOperationException("No root dir");

    var sln = clientService.ProjectInfo.SolutionFile;
    if (!string.IsNullOrEmpty(sln) && File.Exists(sln))
    {
      return [.. solutionService.GetProjectsFromSolutionFile(sln).Select(x => x.AbsolutePath)];
    }

    return [.. Directory.GetFiles(clientService.ProjectInfo.RootDir, "*.csproj", SearchOption.AllDirectories).Order()];
  }
}
