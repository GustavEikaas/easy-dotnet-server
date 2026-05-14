using System.Text.Json;
using EasyDotnet.IDE.Framework;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.MsBuild.Build;
using EasyDotnet.IDE.Models.MsBuild.Project;
using EasyDotnet.IDE.Models.MsBuild.SDK;
using EasyDotnet.MsBuild;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Caching.Memory;

namespace EasyDotnet.IDE.Services;

public class MsBuildService(IVisualStudioLocator locator, IClientService clientService, IProcessQueue processQueue, IMemoryCache memoryCache, INotificationService notificationService, ISolutionService solutionService) : IMsBuildService
{

  private static readonly JsonSerializerOptions JsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };
  private static string GetCacheKeyProperties(string projectPath, string? targetFrameworkMoniker, string configuration) => $"{projectPath}-{targetFrameworkMoniker ?? ""}-{configuration ?? ""}";

  public SdkInstallation[] QuerySdkInstallations()
  {
    MSBuildLocator.AllowQueryAllRuntimeVersions = true;
    var instances = MSBuildLocator.QueryVisualStudioInstances().Where(x => x.DiscoveryType == DiscoveryType.DotNetSdk).ToList();
    return [.. instances.Select(x => new SdkInstallation(x.Name, $"net{x.Version.Major}.0", x.Version, x.MSBuildPath, x.VisualStudioRootPath))];
  }

  public string GetVsTestPath()
  {
    var sdk = QuerySdkInstallations();
    return Path.Join(sdk.ToList()[0].MSBuildPath, "vstest.console.dll");
  }

  public string GetDotnetSdkBasePath() => Path.GetDirectoryName(Path.GetDirectoryName(QuerySdkInstallations().First().MSBuildPath))!;

  public async Task<BuildResult> RequestBuildAsync(
         string targetPath,
         string? targetFrameworkMoniker,
         string? buildArgs,
         string? configuration,
         CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(targetPath))
    {
      throw new ArgumentException("Target path must be provided", nameof(targetPath));
    }

    var (command, args) = await GetBuildAndRestoreCommandAndArguments(clientService.UseVisualStudio ? MSBuildProjectType.VisualStudio : MSBuildProjectType.SDK, targetPath, targetFrameworkMoniker, configuration, buildArgs);

    var (success, stdout, stderr) = await processQueue.RunProcessAsync(command, args, new ProcessOptions(true), cancellationToken);

    var (errors, warnings) = MsBuildBuildStdoutParser.ParseBuildOutput(stdout, stderr);
    var (errorsWithProject, warningsWithProject) = await AddProjectToBuildMessages(targetPath, errors, warnings, cancellationToken);

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

  private async Task<(List<BuildMessageWithProject>, List<BuildMessageWithProject>)> AddProjectToBuildMessages(
    string targetPath,
    IEnumerable<MsBuildStdoutMessage> errors,
    IEnumerable<MsBuildStdoutMessage> warnings,
    CancellationToken cancellationToken)
  {
    if (!errors.Any() && !warnings.Any())
    {
      return ([], []);
    }

    var projectMap = await GetProjectMap(targetPath, cancellationToken);

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

  private async Task<Dictionary<string, string>> GetProjectMap(string targetPath, CancellationToken cancellationToken)
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
      return (await solutionService.GetProjectsFromSolutionFile(targetPath, cancellationToken))
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


  public async Task<List<PackageReference>> GetPackageReferencesAsync(string projectPath, string targetFramework, CancellationToken cancellationToken = default)
  {
    var (success, stdOut, stdErr) = await processQueue.RunProcessAsync(
        "dotnet",
        $"list \"{projectPath}\" package --format json",
        new ProcessOptions(true),
        cancellationToken);

    if (!success)
    {
      throw new InvalidOperationException($"Failed to get package references: {stdErr}");
    }

    var output = JsonSerializer.Deserialize<DotnetListPackageOutput>(stdOut, JsonSerializerOptions)
            ;

    if (output?.Projects == null)
    {
      return [];
    }

    return [.. output.Projects
        .SelectMany(p => p.Frameworks)
        .Where(f => f.Framework.Equals(targetFramework, StringComparison.OrdinalIgnoreCase))
        .SelectMany(f => f.TopLevelPackages ?? Enumerable.Empty<PackageReference>())];
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

  private async Task<(string Command, string Arguments)> GetCommandAndArguments(
      MSBuildProjectType type,
      string targetPath,
      string? targetFrameworkMoniker,
      string? configuration, string? args)
  {
    var tfmArg = string.IsNullOrWhiteSpace(targetFrameworkMoniker)
        ? string.Empty
        : $" /p:TargetFramework={targetFrameworkMoniker}";

    var config = string.IsNullOrEmpty(configuration) ? "" : $"/p:Configuration={configuration}";

    return type switch
    {
      MSBuildProjectType.SDK => ("dotnet", $"msbuild \"{targetPath}\" {config} {tfmArg} {args ?? ""}"),
      MSBuildProjectType.VisualStudio => (await locator.GetVisualStudioMSBuildPath(), $"\"{targetPath}\" {config} {tfmArg} {args ?? ""}"),
      _ => throw new InvalidOperationException("Unknown MSBuild type")
    };
  }

  private async Task<(string Command, string Arguments)> GetBuildAndRestoreCommandAndArguments(
      MSBuildProjectType type,
      string targetPath,
      string? targetFrameworkMoniker,
      string? configuration, string? args)
  {
    var tfmArg = string.IsNullOrWhiteSpace(targetFrameworkMoniker)
        ? string.Empty
        : $" /p:TargetFramework={targetFrameworkMoniker}";

    var config = string.IsNullOrEmpty(configuration) ? "" : $"/p:Configuration={configuration}";

    return type switch
    {
      MSBuildProjectType.SDK => ("dotnet", $"msbuild \"{targetPath}\" {config} {tfmArg} /t:restore /t:build {args ?? ""}"),
      MSBuildProjectType.VisualStudio => (await locator.GetVisualStudioMSBuildPath(), $"\"{targetPath}\" {config} {tfmArg} /t:restore /t:build {args ?? ""}"),
      _ => throw new InvalidOperationException("Unknown MSBuild type")
    };
  }
}