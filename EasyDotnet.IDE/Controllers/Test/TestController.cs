using System.IO.Abstractions;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.IDE.Utils;
using EasyDotnet.Infrastructure.Settings;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Test;

public class TestController(
  GlobalJsonService globalJsonService,
  IClientService clientService,
  IFileSystem fileSystem,
  SettingsService settingsService,
  IEditorService editorService,
  ISolutionService solutionService) : BaseController
{
  [JsonRpcMethod("test/set-project-run-settings")]
  public async Task SetRunSettings(CancellationToken cancellationToken)
  {
    clientService.ThrowIfNotInitialized();
    if (clientService.ProjectInfo?.SolutionFile is null)
    {
      throw new Exception("No solution file found");
    }

    var projects = (await solutionService.GetProjectsFromSolutionFile(clientService.ProjectInfo.SolutionFile, cancellationToken)).Select(x => new SelectionOption(x.AbsolutePath, x.ProjectName)).ToArray();
    if (projects.Length == 0)
    {
      await editorService.DisplayMessage("No projects found");
      return;
    }

    var project = await editorService.RequestSelection("Select project", projects, null);
    if (project is null)
    {
      return;
    }

    var files = Directory.EnumerateFiles(clientService.ProjectInfo.RootDir, "*.runsettings", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

    var choices = files.Select(x => new SelectionOption(x, fileSystem.Path.GetFileName(x))).ToArray();

    if (choices.Length == 0)
    {
      await editorService.DisplayMessage("No runsettings files found");
      return;
    }

    var selection = await editorService.RequestSelection("Pick run settings file", choices, null);
    if (selection is null)
    {
      return;
    }

    settingsService.SetProjectRunSettings(project.Id, selection.Id);
  }

  [JsonRpcMethod("test/solution-command")]
  public RunCommand GetTestCommandForSolution()
  {
    var solutionFile = clientService.RequireSolutionFile();
    var globalJson = globalJsonService.GetGlobalJson();
    var isMicrosoftTestingPlatformRunner = globalJson.IsMicrosoftTestingPlatformRunner();

    if (isMicrosoftTestingPlatformRunner)
    {
      return new("dotnet", ["test", "--solution", solutionFile], ".", []);
    }
    else
    {
      return new("dotnet", ["test", solutionFile], ".", []);
    }
  }
}