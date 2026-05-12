using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Editor;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client.Prompt;
using EasyDotnet.IDE.Workspace.Controllers;
using EasyDotnet.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspaceNugetService(
    IClientService clientService,
    IEditorService editorService,
    WorkspaceBuildHostManager buildHostManager,
    WorkspaceBuildService buildService,
    NugetService nugetService,
    IProgressScopeFactory progressScopeFactory,
    ILogger<WorkspaceNugetService> logger)
{
  private const string Configuration = "Release";
  private const int ProjectSearchDepth = 3;

  public async Task PackAsync(NugetPackRequest request, CancellationToken ct)
  {
    var project = await ResolvePackableProjectAsync(ct);
    if (project is null) return;

    await PackProjectAsync(project, ct);
  }

  public async Task PackAndPushAsync(NugetPackRequest request, CancellationToken ct)
  {
    var project = await ResolvePackableProjectAsync(ct);
    if (project is null) return;

    if (!await PackProjectAsync(project, ct)) return;

    var packagePath = ResolvePackagePath(project);
    if (packagePath is null)
    {
      await editorService.DisplayError($"Could not locate produced .nupkg for {project.ProjectName}");
      return;
    }

    var source = await PickSourceAsync();
    if (source is null) return;

    try
    {
      using (progressScopeFactory.Create("Pushing...", $"Pushing {Path.GetFileName(packagePath)} to {source.Name}"))
      {
        await nugetService.PushPackageAsync([packagePath], source.Source, apiKey: null);
      }
      await editorService.DisplayMessage($"Pushed {Path.GetFileName(packagePath)} to {source.Name}");
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Push failed");
      await editorService.DisplayError($"Push failed: {ex.Message}");
    }
  }

  private Task<bool> PackProjectAsync(ValidatedDotnetProject project, CancellationToken ct) =>
      buildService.BuildQuickfixAsync(
          project.ProjectFullPath,
          project.ProjectName,
          Configuration,
          buildTarget: "Pack",
          operationName: "Pack",
          ct);

  private async Task<ValidatedDotnetProject?> ResolvePackableProjectAsync(CancellationToken ct)
  {
    var solutionFile = clientService.ProjectInfo?.SolutionFile;

    List<ValidatedDotnetProject> projects;
    if (solutionFile is not null)
    {
      projects = await buildHostManager.GetProjectsFromSolutionAsync(
          solutionFile, p => p.IsPackable, Configuration, ct);
    }
    else
    {
      projects = await GetPackableProjectsFromRootAsync(ct);
    }

    if (projects.Count == 0)
    {
      await editorService.DisplayError("No packable projects found");
      return null;
    }

    if (projects.Count == 1) return projects[0];

    var options = projects
        .Select(p => new SelectionOption(ProjectKey(p), $"{p.ProjectName} ({p.TargetFramework})"))
        .ToArray();

    var selected = await editorService.RequestSelection("Pick project to pack", options);
    return selected is null ? null : projects.First(p => ProjectKey(p) == selected.Id);
  }

  private async Task<List<ValidatedDotnetProject>> GetPackableProjectsFromRootAsync(CancellationToken ct)
  {
    var rootDir = clientService.RequireRootDir();
    var csprojFiles = Directory.EnumerateFiles(rootDir, "*.csproj", new EnumerationOptions
    {
      MaxRecursionDepth = ProjectSearchDepth,
      RecurseSubdirectories = true
    }).ToArray();

    if (csprojFiles.Length == 0) return [];

    return [.. (await buildHostManager
            .GetProjectPropertiesBatchAsync(new GetProjectPropertiesBatchRequest(csprojFiles, Configuration), ct)
            .ToListAsync(ct))
            .Where(r => r.Success && r.Project?.IsPackable == true)
            .Select(r => r.Project!)];
  }

  private static string? ResolvePackagePath(ValidatedDotnetProject project)
  {
    var raw = project.Raw;
    if (string.IsNullOrWhiteSpace(raw.PackageId) || string.IsNullOrWhiteSpace(raw.Version))
      return null;

    var projectDir = Path.GetDirectoryName(project.ProjectFullPath);
    if (projectDir is null) return null;

    var outDir = raw.PackageOutputPath;
    if (string.IsNullOrWhiteSpace(outDir))
    {
      // Default pack output is bin/<Config>/
      outDir = Path.Combine("bin", Configuration);
    }
    outDir = outDir.Replace('\\', Path.DirectorySeparatorChar);

    var absoluteOutDir = Path.IsPathRooted(outDir) ? outDir : Path.Combine(projectDir, outDir);
    var packagePath = Path.Combine(absoluteOutDir, $"{raw.PackageId}.{raw.Version}.nupkg");
    return File.Exists(packagePath) ? packagePath : null;
  }

  private async Task<NugetPushSource?> PickSourceAsync()
  {
    var sources = nugetService.GetSources();
    if (sources.Count == 0)
    {
      await editorService.DisplayError("No NuGet sources configured");
      return null;
    }

    var options = sources
        .Select(s => new SelectionOption(s.Name, $"{s.Name} ({s.Source})"))
        .ToArray();

    var selected = await editorService.RequestSelection("Pick NuGet source", options);
    if (selected is null) return null;

    var picked = sources.First(s => s.Name == selected.Id);
    return new NugetPushSource(picked.Name, picked.Source);
  }

  private static string ProjectKey(ValidatedDotnetProject p) => $"{p.ProjectFullPath}:{p.TargetFramework}";

  private sealed record NugetPushSource(string Name, string Source);
}