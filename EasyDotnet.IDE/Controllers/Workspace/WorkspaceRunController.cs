using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.Infrastructure;
using EasyDotnet.Infrastructure.Services;
using EasyDotnet.Infrastructure.Settings;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Workspace;

public record WorkspaceRunRequest(bool UseDefault, bool UseExternalTerminal);

public class WorkspaceRunController(
  ClientService clientService,
  IBuildHostManager buildHostManager,
  ISolutionService solutionService,
  IEditorService editorService,
  SettingsService settingsService) : BaseController
{
  [JsonRpcMethod("workspace/run", UseSingleObjectParameterDeserialization = true)]
  public async Task Run(WorkspaceRunRequest request, CancellationToken cancellationToken)
  {
    var rootDir = clientService.RequireRootDir();
    var solutionFile = clientService.ProjectInfo?.SolutionFile;

    var (targetPath, targetName) = await ResolveProjectAsync(
        request.UseDefault,
        solutionFile,
        rootDir,
        cancellationToken);

    if (request.UseDefault && settingsService.GetDefaultStartupProject() != targetName)
    {
      settingsService.SetDefaultStartupProject(targetName);
    }

    var success = await editorService.BuildProject(targetPath, cancellationToken);
    if (!success)
    {
      return;
    }


  }

  private async Task<(string AbsolutePath, string ProjectName)> ResolveProjectAsync(
        bool useDefault,
        string? solutionFile,
        string rootDir,
        CancellationToken ct)
  {
    var candidates = await GetCandidateProjectsAsync(solutionFile, rootDir, ct);

    if (candidates.Count == 0)
    {
      throw new InvalidOperationException("No valid .csproj or .fsproj projects found to run.");
    }

    if (candidates.Count == 1)
    {
      return candidates[0];
    }

    if (useDefault)
    {
      var defaultName = settingsService.GetDefaultStartupProject();
      var properties = settingsService.GetProjectTargetFramework()
      var match = candidates.FirstOrDefault(p => p.ProjectName == defaultName);

      if (match.AbsolutePath != null)
      {
        return match;
      }
    }

    var selectionOptions = candidates
        .Select(p => new SelectionOption(p.AbsolutePath, p.ProjectName))
        .ToArray();

    var selectedOption = await editorService.RequestSelection("Pick startup project to run", selectionOptions)
        ?? throw new InvalidOperationException("No startup project selected");

    return candidates.First(p => p.AbsolutePath == selectedOption.Id);
  }

  private async Task<List<(string AbsolutePath, string ProjectName)>> GetCandidateProjectsAsync(
      string? solutionFile,
      string rootDir,
      CancellationToken ct)
  {
    if (!string.IsNullOrEmpty(solutionFile) && File.Exists(solutionFile))
    {
      var projects = await solutionService.GetProjectsFromSolutionFile(solutionFile, ct);
      var projectTfms = await buildHostManager.GetProjectPropertiesBatchAsync(new([.. projects.Select(x => x.AbsolutePath)], "Debug"), ct).ToListAsync(ct);
      // projectTfms.Where(x => x.Success && x.Error) -- Should render as (Failed to load)
      // projectTfms.Where(x => x.Project.OutputType.ToLower() == 'exe') 
      return projects.ConvertAll(p => (p.AbsolutePath, p.ProjectName));
    }

    return [.. Directory.EnumerateFiles(rootDir, "*.*proj")
        .Where(f => f.EndsWith(".csproj") || f.EndsWith(".fsproj"))
        .Select(f => (f, Path.GetFileNameWithoutExtension(f)))];
  }
}