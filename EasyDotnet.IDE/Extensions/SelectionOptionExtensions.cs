using System;
using System.IO;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.Domain.Models.Workspace;
using EasyDotnet.MsBuild;

namespace EasyDotnet.IDE.Extensions;

public static class SelectionOptionExtensions
{
  public static SelectionOption<ProjectEntry> ToSelectionOption(this ProjectEntry entry) =>
      entry.Match(
          loaded => loaded.Project.FromDotnetProject() with { Data = entry },
          unloaded => unloaded.FromUnloaded() with { Data = entry },
          errored => errored.FromErrored() with { Data = entry }
      );

  private static SelectionOption<ProjectEntry> FromUnloaded(this ProjectEntry.Unloaded unloaded)
  {
    var baseOpt = unloaded.Path.FromProjectPath<ProjectEntry>();
    return baseOpt with { Display = $"󰔟 {baseOpt.Display} (loading...)" };
  }

  private static SelectionOption<ProjectEntry> FromErrored(this ProjectEntry.Errored errored)
  {
    var fileName = Path.GetFileNameWithoutExtension(errored.Path);
    return new SelectionOption<ProjectEntry>(
        errored.Path,
        $"󰅙 {fileName} (load failed)"
    );
  }

  public static SelectionOption<ProjectEntry> FromDotnetProject(this DotnetProject project)
  {
    var projectPath = project.MSBuildProjectFullPath
        ?? throw new InvalidOperationException("Project must have a file path");

    var display = project.MSBuildProjectName
        ?? project.ProjectName
        ?? Path.GetFileNameWithoutExtension(projectPath);

    if (project.Version != null)
    {
      display += $"@{project.Version}";
    }

    if (project.Language?.Equals("csharp", StringComparison.OrdinalIgnoreCase) == true)
      display += " 󰙱";
    else if (project.Language?.Equals("fsharp", StringComparison.OrdinalIgnoreCase) == true)
      display += " 󰫳";

    if (project.IsTestProject) display += " 󰙨";
    if (project.GeneratePackageOnBuild || project.IsPackable) display += " ";
    if (project.UsingMicrosoftNETSdkWeb) display += " 󱂛";
    if (project.IsRunnable()) display += " 󰆍";
    if (project.UsingMicrosoftNETSdkWorker) display += " ";
    if (project.OutputType?.Equals("WinExe", StringComparison.OrdinalIgnoreCase) == true) display += " ";

    return new SelectionOption<ProjectEntry>(projectPath, display);
  }

  public static SelectionOption<T> FromSolutionFile<T>(this string solutionFilePath)
  {
    if (string.IsNullOrEmpty(solutionFilePath))
      throw new ArgumentException("Path cannot be empty", nameof(solutionFilePath));

    var fileName = Path.GetFileName(solutionFilePath);
    return new SelectionOption<T>(solutionFilePath, $"{fileName} 󰘐");
  }

  public static SelectionOption<T> FromProjectPath<T>(this string projectPath)
  {
    if (string.IsNullOrEmpty(projectPath))
      throw new ArgumentException("Path cannot be empty", nameof(projectPath));

    var fileName = Path.GetFileNameWithoutExtension(projectPath);
    var ext = Path.GetExtension(projectPath);

    var languageIcon = ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ? "󰙱" : "󰫳";
    return new SelectionOption<T>(projectPath, $"{fileName} {languageIcon}");
  }
}