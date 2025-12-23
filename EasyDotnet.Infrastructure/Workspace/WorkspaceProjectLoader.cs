using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.Domain.Models.Workspace;
using EasyDotnet.MsBuild;
using ZiggyCreatures.Caching.Fusion;

namespace EasyDotnet.Infrastructure.Workspace;

public class WorkspaceProjectLoader(IFusionCache cache, IVisualStudioLocator locator, IClientService clientService, IProcessQueue processQueue)
{

  public async Task<ProjectEntry> GetOrLoadAsync(string path, string? targetFrameworkMoniker, TimeSpan softTimeout)
  {
    var project = await cache.GetOrSetAsync<ProjectEntry?>(
        GetCacheKeyProperties(path, targetFrameworkMoniker),
        async (_, cancellationToken) => await GetProjectPropertiesAsync(path, targetFrameworkMoniker, cancellationToken: cancellationToken),
        options =>
        {
          options.AllowTimedOutFactoryBackgroundCompletion = true;
          options.FactoryHardTimeout = softTimeout;
          options.SetDurationMin(15);
        });

    return project ?? new ProjectEntry.Unloaded(path);
  }


  private async Task<ProjectEntry> GetProjectPropertiesAsync(
      string projectPath,
      string? targetFrameworkMoniker = null,
      string configuration = "Debug",
      CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(projectPath))
    {
      throw new ArgumentException("Project path must be provided", nameof(projectPath));
    }

    var props = MsBuildPropertyQueryBuilder.BuildQueryString();

    var (command, args) = await GetCommandAndArguments(
        clientService.UseVisualStudio ? MSBuildProjectType.VisualStudio : MSBuildProjectType.SDK,
        projectPath,
        targetFrameworkMoniker,
        configuration, "");

    args += " -nologo -v:quiet " + props;

    var (success, stdout, _) = await processQueue.RunProcessAsync(command, args, new ProcessOptions(KillOnTimeout: true), cancellationToken);
    if (!success)
    {
      return new ProjectEntry.Errored(projectPath, "Failed to load");
    }
    return new ProjectEntry.Loaded(MsBuildPropertiesStdoutParser.ParseMsBuildOutputToProject(stdout));
  }

  private async Task<(string Command, string Arguments)> GetCommandAndArguments(
      MSBuildProjectType type,
      string targetPath,
      string? targetFrameworkMoniker,
      string configuration, string? args)
  {
    var tfmArg = string.IsNullOrWhiteSpace(targetFrameworkMoniker)
        ? string.Empty
        : $" /p:TargetFramework={targetFrameworkMoniker}";

    return type switch
    {
      MSBuildProjectType.SDK => ("dotnet", $"msbuild \"{targetPath}\" /p:Configuration={configuration} {tfmArg} {args ?? ""}"),
      MSBuildProjectType.VisualStudio => (await locator.GetVisualStudioMSBuildPath(), $"\"{targetPath}\" /p:Configuration={configuration} {tfmArg} {args ?? ""}"),
      _ => throw new InvalidOperationException("Unknown MSBuild type")
    };
  }

  private static string GetCacheKeyProperties(string projectPath, string? targetFrameworkMoniker) => $"{Path.GetFullPath(projectPath)}-{targetFrameworkMoniker ?? ""}".ToLowerInvariant();
}