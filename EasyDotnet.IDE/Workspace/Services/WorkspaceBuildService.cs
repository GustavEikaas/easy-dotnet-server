using EasyDotnet.Application.Interfaces;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.IDE.Workspace.Controllers;
using EasyDotnet.Infrastructure;
using EasyDotnet.Infrastructure.Editor;
using EasyDotnet.Infrastructure.Settings;

namespace EasyDotnet.IDE.Workspace.Services;

public class WorkspaceBuildService(
    IClientService clientService,
    ISolutionService solutionService,
    IBuildHostManager buildHostManager,
    IEditorService editorService,
    IProgressScopeFactory progressScopeFactory,
    SettingsService settingsService)
{
  public async Task BuildProjectAsync(WorkspaceBuildRequest request, CancellationToken ct)
  {
    var solutionFile = clientService.ProjectInfo?.SolutionFile;

    if (solutionFile is not null)
    {
      await BuildProjectWithSolutionAsync(solutionFile, request, ct);
      return;
    }

    await BuildProjectNoSolutionAsync(request, ct);
  }

  public async Task BuildSolutionAsync(WorkspaceBuildRequest request, CancellationToken ct)
  {
    var solutionFile = clientService.RequireSolutionFile();
    await ExecuteBuildAsync(solutionFile, request, ct);
  }

  private async Task BuildProjectWithSolutionAsync(string solutionFile, WorkspaceBuildRequest request, CancellationToken ct)
  {
    if (request.UseDefault)
    {
      var defaultPath = settingsService.GetDefaultBuildProject(solutionFile);
      if (defaultPath is not null && File.Exists(defaultPath))
      {
        await ExecuteBuildAsync(defaultPath, request, ct);
        return;
      }

      settingsService.SetDefaultBuildProject(null);
    }

    var projects = await solutionService.GetProjectsFromSolutionFile(solutionFile, ct);

    if (projects.Count == 0)
    {
      await editorService.DisplayError("No projects found in solution");
      return;
    }

    var options = new List<SelectionOption>
        {
            new(solutionFile, "Solution")
        };
    options.AddRange(projects.Select(p => new SelectionOption(p.AbsolutePath, p.ProjectName)));

    var selected = await editorService.RequestSelection("Pick project to build", [.. options]);
    if (selected is null) return;

    settingsService.SetDefaultBuildProject(selected.Id);

    await ExecuteBuildAsync(selected.Id, request, ct);
  }

  private async Task BuildProjectNoSolutionAsync(WorkspaceBuildRequest request, CancellationToken ct)
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

    if (csprojFiles.Count == 1)
    {
      await ExecuteBuildAsync(csprojFiles[0], request, ct);
      return;
    }

    var options = csprojFiles
        .Select(p => new SelectionOption(p, Path.GetFileNameWithoutExtension(p)))
        .ToArray();

    var selected = await editorService.RequestSelection("Pick project to build", options);
    if (selected is null) return;

    await ExecuteBuildAsync(selected.Id, request, ct);
  }

  private async Task ExecuteBuildAsync(string targetPath, WorkspaceBuildRequest request, CancellationToken ct)
  {
    var name = Path.GetFileName(targetPath);

    if (request.UseTerminal)
    {
      await RunBuildInTerminalAsync(targetPath, name, request.BuildArgs, ct);
      return;
    }

    await RunBuildQuickfixAsync(targetPath, name, ct);
  }

  private async Task RunBuildInTerminalAsync(string targetPath, string name, string? buildArgs, CancellationToken ct)
  {
    var args = new List<string> { "build", targetPath };
    if (!string.IsNullOrWhiteSpace(buildArgs))
      args.Add(buildArgs);

    var command = new RunCommand(
        "dotnet",
        args,
        Path.GetDirectoryName(targetPath) ?? ".",
        []);

    var exitCode = await editorService.RequestRunCommandAsync(command, ct);
    if (exitCode != 0)
      await editorService.DisplayError($"Build failed for {name} (exit code {exitCode})");
  }

  private async Task RunBuildQuickfixAsync(string targetPath, string name, CancellationToken ct)
  {
    List<BatchBuildResult> results;
    using (progressScopeFactory.Create("Building...", $"Building {name}"))
    {
      results = await buildHostManager
          .BatchBuildAsync(new BatchBuildRequest([targetPath], "Debug"), ct)
          .ToListAsync(ct);
    }

    var finishedResults = results.Where(r => r.Kind == BatchBuildResultKind.Finished).ToList();
    var allDiagnostics = finishedResults
        .SelectMany(r => r.Output?.Diagnostics ?? [])
        .ToList();

    var warningCount = allDiagnostics.Count(d => d.Severity == BuildDiagnosticSeverity.Warning);
    var succeeded = finishedResults.All(r => r.Success == true);

    if (succeeded)
    {
      var successMsg = warningCount > 0
          ? $"Build succeeded — {warningCount} warning(s)"
          : "Build succeeded.";
      await editorService.DisplayMessage(successMsg);
      return;
    }

    var errors = allDiagnostics
        .Where(d => d.Severity == BuildDiagnosticSeverity.Error)
        .Select(d => new QuickFixItem(
            FileName: d.File ?? "",
            LineNumber: d.LineNumber,
            ColumnNumber: d.ColumnNumber,
            Text: string.IsNullOrEmpty(d.Code) ? d.Message ?? "" : $"[{d.Code}] {d.Message}",
            Type: QuickFixItemType.Error))
        .ToArray();

    await editorService.SetQuickFixList(errors);
    await editorService.DisplayError($"Build FAILED — {errors.Length} error(s), {warningCount} warning(s)");
  }
}