using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Models.Client.Prompt;
using EasyDotnet.IDE.Settings;
using EasyDotnet.IDE.Utils;
using EasyDotnet.IDE.Workspace.Controllers;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspaceTestService(
    IClientService clientService,
    IEditorService editorService,
    WorkspaceBuildHostManager buildHostManager,
    SettingsService settingsService,
    GlobalJsonService globalJsonService,
    WorkspacePreBuildService preBuildService)
{
  private const string SolutionOptionId = "__solution__";

  private static string SelectionKey(ValidatedDotnetProject p) =>
      $"{p.ProjectFullPath}:{p.TargetFramework}";

  public async Task TestProjectAsync(WorkspaceTestRequest request, CancellationToken ct)
  {
    var solutionFile = clientService.ProjectInfo?.SolutionFile;

    if (solutionFile is not null)
    {
      await TestProjectWithSolutionAsync(solutionFile, request, ct);
      return;
    }

    await TestProjectNoSolutionAsync(request, ct);
  }

  public async Task TestSolutionAsync(WorkspaceTestRequest request, CancellationToken ct)
  {
    var solutionFile = clientService.RequireSolutionFile();
    await ExecuteTestSolutionAsync(solutionFile, request, ct);
  }

  private async Task TestProjectWithSolutionAsync(string solutionFile, WorkspaceTestRequest request, CancellationToken ct)
  {
    if (request.UseDefault)
    {
      var defaultPath = settingsService.GetDefaultTestProject();
      if (defaultPath is not null && File.Exists(defaultPath))
      {
        var defaultProject = await TryGetProjectAsync(defaultPath, ct);
        if (defaultProject is not null)
        {
          await ExecuteTestAsync(defaultProject, request, ct);
          return;
        }
      }

      settingsService.SetDefaultTestProject(null);
    }

    var testProjects = await buildHostManager.GetTestProjectsFromSolutionAsync(solutionFile, ct: ct);

    if (testProjects.Count == 0)
    {
      await editorService.DisplayError("No test projects found in solution");
      return;
    }

    var options = new List<SelectionOption>
    {
      new(SolutionOptionId, "Solution")
    };
    options.AddRange(testProjects.Select(p => new SelectionOption(SelectionKey(p), $"{p.ProjectName} ({p.TargetFramework})")));

    var selected = await editorService.RequestSelection("Pick test project", [.. options]);
    if (selected is null) return;

    if (selected.Id == SolutionOptionId)
    {
      settingsService.SetDefaultTestProject(solutionFile);
      await ExecuteTestSolutionAsync(solutionFile, request, ct);
      return;
    }

    var project = testProjects.First(p => SelectionKey(p) == selected.Id);
    settingsService.SetDefaultTestProject(project.ProjectFullPath);
    settingsService.SetProjectTargetFramework(project.ProjectFullPath, project.TargetFramework);
    await ExecuteTestAsync(project, request, ct);
  }

  private async Task TestProjectNoSolutionAsync(WorkspaceTestRequest request, CancellationToken ct)
  {
    var rootDir = clientService.RequireRootDir();

    var csprojFiles = Directory.EnumerateFiles(rootDir, "*.csproj", new EnumerationOptions
    {
      MaxRecursionDepth = 3,
      RecurseSubdirectories = true
    }).ToList();

    if (csprojFiles.Count == 0)
    {
      await editorService.DisplayError("No project files found");
      return;
    }

    var evaluated = await buildHostManager.GetProjectPropertiesBatchAsync(new([.. csprojFiles], null), ct).ToListAsync(ct);

    var testProjects = evaluated
        .Where(r => r.Success && r.Project is not null && (r.Project.IsMTP || r.Project.IsVsTest))
        .Select(r => r.Project!)
        .ToList();

    if (testProjects.Count == 0)
    {
      await editorService.DisplayError("No test projects found");
      return;
    }

    if (testProjects.Count == 1)
    {
      await ExecuteTestAsync(testProjects[0], request, ct);
      return;
    }

    var options = testProjects
        .Select(p => new SelectionOption(SelectionKey(p), $"{p.ProjectName} ({p.TargetFramework})"))
        .ToArray();

    var selected = await editorService.RequestSelection("Pick test project", options);
    if (selected is null) return;

    var picked = testProjects.First(p => SelectionKey(p) == selected.Id);
    await ExecuteTestAsync(picked, request, ct);
  }

  private async Task ExecuteTestAsync(ValidatedDotnetProject project, WorkspaceTestRequest request, CancellationToken ct)
  {
    if (!await preBuildService.BuildBeforeRunAsync(project.ProjectFullPath, project.ProjectName, ct))
      return;

    var projectDir = Path.GetDirectoryName(project.ProjectFullPath) ?? ".";
    var isMtp = globalJsonService.GetGlobalJson(projectDir).IsMicrosoftTestingPlatformRunner();

    var args = new List<string> { "test" };
    if (isMtp)
    {
      args.Add("--project");
      args.Add(project.ProjectFullPath);
    }
    else
    {
      args.Add(project.ProjectFullPath);
      args.Add("--framework");
      args.Add(project.TargetFramework);
    }

    args.Add("--no-restore");
    args.Add("--no-build");

    if (!string.IsNullOrWhiteSpace(request.TestArgs))
      args.Add(request.TestArgs);

    var command = new RunCommand("dotnet", [.. args], projectDir, []);
    await editorService.RequestRunCommandAsync(command, ct);
  }

  private async Task ExecuteTestSolutionAsync(string solutionFile, WorkspaceTestRequest request, CancellationToken ct)
  {
    var name = Path.GetFileName(solutionFile);
    if (!await preBuildService.BuildBeforeRunAsync(solutionFile, name, ct))
      return;

    var solutionDir = Path.GetDirectoryName(solutionFile) ?? ".";
    var isMtp = globalJsonService.GetGlobalJson(solutionDir).IsMicrosoftTestingPlatformRunner();

    var args = new List<string> { "test" };
    if (isMtp)
    {
      args.Add("--solution");
      args.Add(solutionFile);
    }
    else
    {
      args.Add(solutionFile);
    }

    args.Add("--no-restore");
    args.Add("--no-build");

    if (!string.IsNullOrWhiteSpace(request.TestArgs))
      args.Add(request.TestArgs);

    var command = new RunCommand("dotnet", [.. args], solutionDir, []);
    await editorService.RequestRunCommandAsync(command, ct);
  }

  private async Task<ValidatedDotnetProject?> TryGetProjectAsync(string projectPath, CancellationToken ct)
  {
    var storedTfm = await settingsService.GetProjectTargetFramework(projectPath, ct);
    if (storedTfm is not null)
    {
      var project = await buildHostManager.GetProjectAsync(projectPath, storedTfm, ct: ct);
      if (project is not null) return project;
    }

    await foreach (var result in buildHostManager.GetProjectPropertiesBatchAsync(new([projectPath], null), ct))
    {
      if (result.Success && result.Project is not null)
        return result.Project;
    }
    return null;
  }
}