using System.IO.Abstractions;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.IDE.Services;
using EasyDotnet.Infrastructure.Settings;
using EasyDotnet.MsBuild;
using EasyDotnet.MTP;
using EasyDotnet.Services;
using EasyDotnet.Types;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Test;

public class TestController(
  ILogger<TestController> logger,
  IClientService clientService,
  MtpService mtpService,
  VsTestService vsTestService,
  IMsBuildService msBuildService,
  IFileSystem fileSystem,
  SettingsService settingsService,
  IEditorService editorService,
  ISolutionService solutionService) : BaseController
{

  [JsonRpcMethod("test/discover")]
  public async Task<IAsyncEnumerable<DiscoveredTest>> Discover(
    string projectPath,
    string? targetFrameworkMoniker = null,
    string? configuration = null,
    CancellationToken token = default)
  {

    if (!clientService.IsInitialized)
    {
      throw new Exception("Client has not initialized yet");
    }
    var project = await GetProject(projectPath, targetFrameworkMoniker, configuration, token);
    if (project.IsMTP())
    {
      var path = GetExecutablePath(project);
      var res = await mtpService.RunDiscoverAsync(path, token);
      return res.AsAsyncEnumerable();
    }
    else
    {
      return (await vsTestService.RunDiscover(project.TargetPath!, token)).ToBatchedAsyncEnumerable(30);
    }
  }

  [JsonRpcMethod("test/debug")]
  public async Task<IAsyncEnumerable<TestRunResult>> Debug(
    string projectPath,
    string configuration,
    RunRequestNode[] filter,
    string? targetFrameworkMoniker = null,
    CancellationToken token = default
  )
  {
    if (!clientService.IsInitialized)
    {
      throw new Exception("Client has not initialized yet");
    }
    var project = await GetProject(projectPath, targetFrameworkMoniker, configuration, token);


    if (project.IsMTP())
    {
      var path = GetExecutablePath(project);

      var res = await WithTimeout(
        (token) => mtpService.DebugTestsAsync(project, path, filter, token),
        TimeSpan.FromMinutes(3),
        token
      );
      return res.AsAsyncEnumerable();
    }
    else
    {
      var runSettingsFile = settingsService.GetProjectRunSettings(projectPath!);
      var runSettings = runSettingsFile is not null ? fileSystem.File.ReadAllText(runSettingsFile) : null;
      logger.LogInformation("Using runsettings {runSettings}", runSettingsFile);
      return (await vsTestService.DebugTests(project, [.. filter.Select(x => Guid.Parse(x.Uid))], runSettings, token)).ToBatchedAsyncEnumerable(30);
    }
  }

  [JsonRpcMethod("test/run")]
  public async Task<IAsyncEnumerable<TestRunResult>> Run(
    string projectPath,
    string configuration,
    RunRequestNode[] filter,
    string? targetFrameworkMoniker = null,
    CancellationToken token = default
  )
  {
    if (!clientService.IsInitialized)
    {
      throw new Exception("Client has not initialized yet");
    }
    var project = await GetProject(projectPath, targetFrameworkMoniker, configuration, token);

    var runSettingsFile = settingsService.GetProjectRunSettings(projectPath!);
    var runSettings = runSettingsFile is not null ? fileSystem.File.ReadAllText(runSettingsFile) : null;
    logger.LogInformation("Using runsettings {runSettings}", runSettingsFile);

    if (project.IsMTP())
    {
      var path = GetExecutablePath(project);

      var res = await WithTimeout(
        (token) => mtpService.RunTestsAsync(path, filter, token),
        TimeSpan.FromMinutes(3),
        token
      );
      return res.AsAsyncEnumerable();
    }
    else
    {
      return (await vsTestService.RunTests(project, [.. filter.Select(x => Guid.Parse(x.Uid))], runSettings, token)).ToBatchedAsyncEnumerable(30);
    }
  }

  [JsonRpcMethod("test/set-project-run-settings")]
  public async Task SetRunSettings()
  {
    clientService.ThrowIfNotInitialized();
    if (clientService.ProjectInfo?.SolutionFile is null)
    {
      throw new Exception("No solution file found");
    }

    var projects = solutionService.GetProjectsFromSolutionFile(clientService.ProjectInfo.SolutionFile).Select(x => new SelectionOption(x.AbsolutePath, x.ProjectName)).ToArray();
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

  private static string GetExecutablePath(DotnetProject project) => OperatingSystem.IsWindows() ? Path.ChangeExtension(project.TargetPath!, ".exe") : project.TargetPath![..^4];

  private async Task<DotnetProject> GetProject(string projectPath, string? targetFrameworkMoniker, string? configuration, CancellationToken cancellationToken)
  {
    var project = await msBuildService.GetProjectPropertiesAsync(projectPath, targetFrameworkMoniker, configuration ?? "Debug", cancellationToken);
    return string.IsNullOrEmpty(project.TargetPath) ? throw new Exception("TargetPath is null or empty") : project;
  }

  private static async Task<T> WithTimeout<T>(
    Func<CancellationToken, Task<T>> func,
    TimeSpan timeout,
    CancellationToken callerToken
  )
  {
    using var timeoutCts = new CancellationTokenSource(timeout);
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
      callerToken,
      timeoutCts.Token
    );

    return await func(linkedCts.Token);
  }


}