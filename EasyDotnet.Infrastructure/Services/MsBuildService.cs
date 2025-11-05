using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.MsBuild.Build;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.Domain.Models.MsBuild.SDK;
using EasyDotnet.MsBuild;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Caching.Memory;

namespace EasyDotnet.Services;

public class MsBuildService(IVisualStudioLocator locator, IClientService clientService, IProcessQueue processQueue, IMemoryCache memoryCache, INotificationService notificationService, ISolutionService solutionService) : IMsBuildService
{
  public SdkInstallation[] QuerySdkInstallations()
  {
    MSBuildLocator.AllowQueryAllRuntimeVersions = true;
    var instances = MSBuildLocator.QueryVisualStudioInstances().Where(x => x.DiscoveryType == DiscoveryType.DotNetSdk).ToList();
    return [.. instances.Select(x => new SdkInstallation(x.Name, $"net{x.Version.Major}.0", x.Version, x.MSBuildPath, x.VisualStudioRootPath))];
  }

  public bool HasMinimumSdk(Version version) => QuerySdkInstallations().Any(x => x.Version >= version);

  public string GetDotnetSdkBasePath() => Path.GetDirectoryName(Path.GetDirectoryName(QuerySdkInstallations().First().MSBuildPath))!;

  public async Task<BuildResult> RequestBuildAsync(
         string targetPath,
         string? targetFrameworkMoniker,
         string? buildArgs,
         string configuration = "Debug",
         CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(targetPath))
    {
      throw new ArgumentException("Target path must be provided", nameof(targetPath));
    }

    var (command, args) = await GetCommandAndArguments(clientService.UseVisualStudio ? MSBuildProjectType.VisualStudio : MSBuildProjectType.SDK, targetPath, targetFrameworkMoniker, configuration, buildArgs);

    var (success, stdout, stderr) = await processQueue.RunProcessAsync(command, args, new ProcessOptions(true), cancellationToken);

    var (errors, warnings) = MsBuildBuildStdoutParser.ParseBuildOutput(stdout, stderr);
    var (errorsWithProject, warningsWithProject) = AddProjectToBuildMessages(targetPath, errors, warnings);

    var orderedErrors = errorsWithProject
        .OrderBy(e => e.Project)
        .ThenBy(e => e.FilePath)
        .ThenBy(e => e.LineNumber)
        .ThenBy(e => e.ColumnNumber)
        .ToList();

    var orderedWarnings = warningsWithProject
        .OrderBy(w => w.Project)
        .ThenBy(w => w.FilePath)
        .ThenBy(w => w.LineNumber)
        .ThenBy(w => w.ColumnNumber)
        .ToList();

    return new BuildResult(
            success,
            orderedErrors,
            orderedWarnings
        );
  }

  private static string NormalizePath(string path)
  {
    var fullPath = Path.GetFullPath(path);
    return fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
  }

  private (List<BuildMessageWithProject>, List<BuildMessageWithProject>) AddProjectToBuildMessages(
    string targetPath,
    IEnumerable<MsBuildStdoutMessage> errors,
    IEnumerable<MsBuildStdoutMessage> warnings)
  {
    if (!errors.Any() && !warnings.Any())
    {
      return ([], []);
    }

    var projectMap = GetProjectMap(targetPath);

    var map = new Func<IEnumerable<MsBuildStdoutMessage>, List<BuildMessageWithProject>>(messages =>
        messages.Select(m => new BuildMessageWithProject(
            m.Type,
            m.FilePath,
            m.LineNumber,
            m.ColumnNumber,
            m.Code,
            m.Message,
            AssignProject(m.FilePath, targetPath, projectMap)
        )).ToList()
    );

    return (map(errors), map(warnings));
  }


  private static string? AssignProject(string filePath, string targetPath, Dictionary<string, string> projectMap)
  {
    var normalizedFilePath = NormalizePath(filePath);
    var ext = Path.GetExtension(targetPath);


    if (FileTypes.IsCsProjectFile(targetPath))
      return Path.GetFileNameWithoutExtension(targetPath);

    if (FileTypes.IsSolutionFile(targetPath))
    {
      return projectMap
        .Where(kvp => normalizedFilePath.StartsWith(kvp.Value, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(kvp => kvp.Value.Length)
        .Select(kvp => kvp.Key)
        .FirstOrDefault();
    }

    return null;
  }


  private Dictionary<string, string> GetProjectMap(string targetPath)
  {
    if (FileTypes.IsAnyProjectFile(targetPath))
    {
      return new Dictionary<string, string>
      {
        [Path.GetFileNameWithoutExtension(targetPath)] =
              NormalizePath(Path.GetDirectoryName(targetPath) ?? string.Empty)
      };
    }

    if (FileTypes.IsAnySolutionFile(targetPath))
    {
      return solutionService.GetProjectsFromSolutionFile(targetPath)
          .ToDictionary(
              p => Path.GetFileNameWithoutExtension(p.AbsolutePath),
              p => NormalizePath(Path.GetDirectoryName(p.AbsolutePath) ?? string.Empty)
          );
    }

    throw new InvalidOperationException("Target must be a .csproj or (.sln, .slnx) file");
  }




  public async Task InvalidateProjectProperties(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug")
  {
    memoryCache.Remove(GetCacheKeyProperties(projectPath, targetFrameworkMoniker, configuration));
    await notificationService.NotifyProjectChanged(projectPath, targetFrameworkMoniker, configuration);
  }

  public async Task<DotnetProject> GetOrSetProjectPropertiesAsync(
      string projectPath,
      string? targetFrameworkMoniker = null,
      string configuration = "Debug",
      CancellationToken cancellationToken = default) => await memoryCache.GetOrCreateAsync(
        GetCacheKeyProperties(projectPath, targetFrameworkMoniker, configuration),
        _ => GetProjectPropertiesAsync(projectPath, targetFrameworkMoniker, configuration, cancellationToken)
    ) ?? throw new Exception("Failed to get project properties");

  private static string GetCacheKeyProperties(string projectPath, string? targetFrameworkMoniker, string configuration) => $"{projectPath}-{targetFrameworkMoniker ?? ""}-{configuration ?? ""}";

  public async Task<DotnetProject> GetProjectPropertiesAsync(
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

    var (success, stdout, stderr) = await processQueue.RunProcessAsync(command, args, new ProcessOptions(KillOnTimeout: true), cancellationToken);
    if (!success)
    {
      throw new InvalidOperationException($"Failed to get project properties: {stderr}");
    }
    return MsBuildPropertiesStdoutParser.ParseMsBuildOutputToProject(stdout);
  }

  public async Task<List<string>> GetProjectReferencesAsync(string projectPath, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(projectPath))
    {
      throw new ArgumentException("Project path must be provided", nameof(projectPath));
    }

    var (success, stdOut, stdErr) = await processQueue.RunProcessAsync(
           "dotnet",
           $"list \"{projectPath}\" reference",
           new ProcessOptions(true),
           cancellationToken);

    if (!success)
    {
      throw new InvalidOperationException($"Failed to get project references: {stdErr}");
    }

    var projectDir = Path.GetDirectoryName(projectPath)!;

    return [.. stdOut
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Where(line => line.EndsWith(FileTypes.CsProjectExtension, StringComparison.OrdinalIgnoreCase) || line.EndsWith(FileTypes.FsProjectExtension, StringComparison.OrdinalIgnoreCase))
        .Select(relativePath => Path.GetFullPath(Path.Combine(projectDir, relativePath)))];
  }


  public async Task<bool> AddProjectReferenceAsync(string projectPath, string targetPath, CancellationToken cancellationToken = default)
  {
    var (success, _, _) = await processQueue.RunProcessAsync(
           "dotnet",
           $"add \"{projectPath}\" reference \"{targetPath}\"",
           new ProcessOptions(true),
           cancellationToken);

    return success;
  }

  public async Task<bool> RemoveProjectReferenceAsync(string projectPath, string targetPath, CancellationToken cancellationToken = default)
  {
    var (success, _, _) = await processQueue.RunProcessAsync(
           "dotnet",
           $"remove \"{projectPath}\" reference \"{targetPath}\"",
           new ProcessOptions(true),
           cancellationToken);

    return success;
  }

  public async Task<string> BuildRunCommand(bool isSdk, DotnetProject project)
  {
    var buildCmd = await BuildBuildCommand(isSdk, project);

    var useIISExpress = project.UseIISExpress;
    return (isSdk, useIISExpress) switch
    {
      (true, _) => $"dotnet run --project \"{project.MSBuildProjectFullPath}\"",

      (false, true) =>
          $"{buildCmd}; & \"{locator.GetIisExpressExe()}\" /config:\"{locator.GetApplicationHostConfig()}\" /site:\"{project.MSBuildProjectName}\"",

      (false, false) => $"\"{project.TargetPath}\""
    };
  }

  public string BuildTestCommand(bool isSdk, DotnetProject project) => isSdk switch
  {
    true => $"dotnet test \"{project.MSBuildProjectFullPath}\"",
    false => $"dotnet vstest \"{project.TargetPath}\""
  };

  public async Task<string> BuildBuildCommand(bool isSdk, DotnetProject project)
  {
    var normalizedPath = project.MSBuildProjectFullPath;

    return isSdk
        ? $"dotnet build \"{normalizedPath}\""
        : $"& \"{await locator.GetVisualStudioMSBuildPath()}\" \"{normalizedPath}\"";
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

}