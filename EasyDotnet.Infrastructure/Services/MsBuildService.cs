using System.Text.Json;
using System.Text.RegularExpressions;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.MsBuild.Build;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.Domain.Models.MsBuild.SDK;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Caching.Memory;

namespace EasyDotnet.Services;

public partial class MsBuildService(IVisualStudioLocator locator, IClientService clientService, IProcessQueue processQueue, IMemoryCache memoryCache, INotificationService notificationService, ISolutionService solutionService) : IMsBuildService
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

    var (errors, warnings) = ParseBuildOutput(stdout, stderr);
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
    IEnumerable<BuildMessage> errors,
    IEnumerable<BuildMessage> warnings)
  {
    if (!errors.Any() && !warnings.Any())
    {
      return ([], []);
    }

    var projectMap = GetProjectMap(targetPath);

    var map = new Func<IEnumerable<BuildMessage>, List<BuildMessageWithProject>>(messages =>
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

    if (ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
      return Path.GetFileNameWithoutExtension(targetPath);

    if (ext.Equals(".sln", StringComparison.OrdinalIgnoreCase))
    {
      return projectMap
        .Where(kvp => normalizedFilePath.StartsWith(kvp.Value, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(kvp => kvp.Value.Length)
        .Select(kvp => kvp.Key)
        .FirstOrDefault();
    }

    return null;
  }

  private Dictionary<string, string> GetProjectMap(string targetPath) =>
    Path.GetExtension(targetPath).ToLowerInvariant() switch
    {
      ".csproj" => new Dictionary<string, string>
      {
        [Path.GetFileNameWithoutExtension(targetPath)] = NormalizePath(Path.GetDirectoryName(targetPath) ?? "")
      },
      ".sln" => solutionService.GetProjectsFromSolutionFile(targetPath)
               .ToDictionary(
                   p => Path.GetFileNameWithoutExtension(p.AbsolutePath),
                   p => NormalizePath(Path.GetDirectoryName(p.AbsolutePath) ?? "")
               ),
      _ => throw new InvalidOperationException("Target must be a .csproj or .sln file")
    };

  private class MsBuildPropertiesResponse
  {
    public Dictionary<string, string?> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  }

  private static string GetLanguage(string path) => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? "csharp" :
    path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ? "fsharp" :
    "unknown";


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

    var propsToQuery = new[]
    {
        "OutputPath", "OutputType", "TargetExt", "AssemblyName",
        "TargetFramework", "TargetFrameworks", "IsTestProject", "UserSecretsId",
        "TestingPlatformDotnetTestSupport", "TargetPath", "GeneratePackageOnBuild",
        "IsPackable", "PackageId", "Version", "PackageOutputPath", "TargetFrameworkVersion",
        "UsingMicrosoftNETSdkWorker", "UsingMicrosoftNETSdkWeb",  "UseIISExpress", "LangVersion",
        "RootNamespace", "IsAspireHost", "AspireHostingSDKVersion"
    };

    var (command, args) = await GetCommandAndArguments(
        clientService.UseVisualStudio ? MSBuildProjectType.VisualStudio : MSBuildProjectType.SDK,
        projectPath,
        targetFrameworkMoniker,
        configuration, "");

    args += " -nologo -v:quiet " + string.Join(" ", propsToQuery.Select(p => $"-getProperty:{p}"));

    var (success, stdout, stderr) = await processQueue.RunProcessAsync(command, args, new ProcessOptions(KillOnTimeout: true), cancellationToken);
    if (!success)
      throw new InvalidOperationException($"Failed to get project properties: {stderr}");

    var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    var jsonStartIndex = Array.FindIndex(lines, line => line.Trim() == "{");

    if (jsonStartIndex == -1)
    {
      throw new InvalidOperationException("Did not find JSON payload in MSBuild output.");
    }

    var jsonPayload = string.Join("\n", lines.Skip(jsonStartIndex));

    var msbuildOutput = JsonSerializer.Deserialize<MsBuildPropertiesResponse>(jsonPayload);
    var values = msbuildOutput?.Properties ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    string? TryGet(string name)
        => values.TryGetValue(name, out var v) && !string.IsNullOrEmpty(v) ? v : null;
    bool TryGetBool(string name) => values.TryGetValue(name, out var v) && bool.TryParse(v, out var b) && b;

    var tfm = TryGet("TargetFramework");
    var versionMoniker = tfm is { } s && s.StartsWith("net", StringComparison.OrdinalIgnoreCase)
        ? s[3..]
        : tfm;

    var nugetVersion = TryGet("Version");
    var targetFrameworkVersion = TryGet("TargetFrameworkVersion");
    var isNetFramework = targetFrameworkVersion?.StartsWith("v4") == true;
    var useIISExpress = TryGetBool("UseIISExpress");
    var targetPath = TryGet("TargetPath");
    var projectName = Path.GetFileNameWithoutExtension(projectPath);
    var aspireSdkVersionString = TryGet("AspireHostingSDKVersion");
    var aspireSdkVersion = TryParseVersion(aspireSdkVersionString);

    return new DotnetProject(
        ProjectName: projectName,
        Language: GetLanguage(projectPath),
        OutputPath: TryGet("OutputPath"),
        OutputType: TryGet("OutputType"),
        TargetExt: TryGet("TargetExt"),
        AssemblyName: TryGet("AssemblyName"),
        TargetFramework: tfm,
        TargetFrameworks: TryGet("TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries),
        IsTestProject: TryGetBool("IsTestProject"),
        IsWebProject: TryGetBool("UsingMicrosoftNETSdkWeb"),
        IsWorkerProject: TryGetBool("UsingMicrosoftNETSdkWorker"),
        UserSecretsId: TryGet("UserSecretsId"),
        TestingPlatformDotnetTestSupport: TryGetBool("TestingPlatformDotnetTestSupport"),
        TargetPath: TryGet("TargetPath"),
        GeneratePackageOnBuild: TryGetBool("GeneratePackageOnBuild"),
        IsPackable: TryGetBool("IsPackable"),
        LangVersion: TryGet("LangVersion"),
        RootNamespace: TryGet("RootNamespace"),
        PackageId: TryGet("PackageId"),
        NugetVersion: string.IsNullOrWhiteSpace(nugetVersion) ? null : nugetVersion,
        Version: targetFrameworkVersion,
        PackageOutputPath: TryGet("PackageOutputPath"),
        IsMultiTarget: (TryGet("TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries).Length ?? 0) > 1,
        IsNetFramework: isNetFramework,
        UseIISExpress: useIISExpress,
        RunCommand: await BuildRunCommand(!isNetFramework, useIISExpress, targetPath, projectPath, projectName),
        BuildCommand: await BuildBuildCommand(!isNetFramework, projectPath),
        TestCommand: BuildTestCommand(!isNetFramework, targetPath, projectPath),
        IsAspireHost: TryGetBool("IsAspireHost"),
        AspireHostingSdkVersion: aspireSdkVersion);
  }
  private static Version? TryParseVersion(string? versionString) =>
    string.IsNullOrWhiteSpace(versionString) ? null : Version.TryParse(versionString, out var version) ? version : null;

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
        .Where(line => line.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || line.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
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

  private async Task<string> BuildRunCommand(bool isSdk, bool useIISExpress, string? targetPath, string projectPath, string projectName)
  {
    var buildCmd = await BuildBuildCommand(isSdk, projectPath);

    return (isSdk, useIISExpress) switch
    {
      (true, _) => $"dotnet run --project \"{projectPath}\"",

      (false, true) =>
          $"{buildCmd}; & \"{locator.GetIisExpressExe()}\" /config:\"{locator.GetApplicationHostConfig()}\" /site:\"{projectName}\"",

      (false, false) => $"\"{targetPath}\""
    };
  }

  private static string BuildTestCommand(bool isSdk, string? targetPath, string projectPath) => isSdk switch
  {
    true => $"dotnet test \"{projectPath}\"",
    false => $"dotnet vstest \"{targetPath}\""
  };

  private async Task<string> BuildBuildCommand(bool isSdk, string projectPath)
  {
    var normalizedPath = Path.GetFullPath(projectPath)
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

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

  private static (List<BuildMessage> Errors, List<BuildMessage> Warnings) ParseBuildOutput(string stdout, string stderr)
  {
    var regex = MsBuildLoggingLine();

    var messages =
        stdout
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => regex.Match(line))
            .Where(match => match.Success)
            .Select(match => new BuildMessage(
                Type: match.Groups["type"].Value,
                FilePath: match.Groups["file"].Value.Trim(), // strip leading spaces
                LineNumber: int.Parse(match.Groups["line"].Value),
                ColumnNumber: int.Parse(match.Groups["col"].Value),
                Code: match.Groups["code"].Value,
                Message: match.Groups["msg"].Value
            ));

    var stderrMessages =
        stderr
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new BuildMessage("error", "", 0, 0, "", line));

    var allMessages = messages.Concat(stderrMessages);

    var errors = allMessages
        .Where(m => m.Type.Equals("error", StringComparison.OrdinalIgnoreCase))
        .GroupBy(m => (m.Type, m.Code, m.LineNumber, m.ColumnNumber))
        .Select(g => g.First())
        .ToList();

    var warnings = allMessages
        .Where(m => m.Type.Equals("warning", StringComparison.OrdinalIgnoreCase))
        .GroupBy(m => (m.Type, m.Code, m.LineNumber, m.ColumnNumber))
        .Select(g => g.First())
        .ToList();

    return (errors, warnings);
  }

  [GeneratedRegex(@"^(?<file>.*)\((?<line>\d+),(?<col>\d+)\): (?<type>error|warning) (?<code>\S+): (?<msg>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
  private static partial Regex MsBuildLoggingLine();
}