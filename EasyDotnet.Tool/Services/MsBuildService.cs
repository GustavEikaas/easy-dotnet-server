using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Locator;

namespace EasyDotnet.Services;

public sealed record BuildMessage(string Type, string FilePath, int LineNumber, int ColumnNumber, string Code, string? Message);
public sealed record SdkInstallation(string Name, string Moniker, Version Version, string MSBuildPath, string VisualStudioRootPath);
public sealed record BuildResult(bool Success, List<BuildMessage> Errors, List<BuildMessage> Warnings);

public sealed record DotnetProjectProperties(
    string ProjectName,
    string Language,
    string? OutputPath,
    string? OutputType,
    string? TargetExt,
    string? AssemblyName,
    string? TargetFramework,
    string[]? TargetFrameworks,
    bool IsTestProject,
    bool IsWebProject,
    bool IsWorkerProject,
    string? UserSecretsId,
    bool TestingPlatformDotnetTestSupport,
    string? TargetPath,
    bool GeneratePackageOnBuild,
    bool IsPackable,
    string? PackageId,
    string? NugetVersion,
    string? Version,
    string? PackageOutputPath,
    bool IsMultiTarget,
    bool IsNetFramework,
    bool UseIISExpress,
    string RunCommand,
    string BuildCommand,
    string TestCommand
);

public partial class MsBuildService(VisualStudioLocator locator, ClientService clientService)
{
  public static SdkInstallation[] QuerySdkInstallations()
  {
    MSBuildLocator.AllowQueryAllRuntimeVersions = true;
    var instances = MSBuildLocator.QueryVisualStudioInstances().Where(x => x.DiscoveryType == DiscoveryType.DotNetSdk).ToList();
    var monikers = instances.Select(x => new SdkInstallation(x.Name, $"net{x.Version.Major}.0", x.Version, x.MSBuildPath, x.VisualStudioRootPath)).ToArray();
    return monikers;
  }

  public async Task<BuildResult> RequestBuildAsync(
         string targetPath,
         string? targetFrameworkMoniker,
         string configuration = "Debug",
         CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(targetPath))
    {
      throw new ArgumentException("Target path must be provided", nameof(targetPath));
    }

    var (command, args) = GetCommandAndArguments(clientService.UseVisualStudio ? MSBuildType.VisualStudio : MSBuildType.SDK, targetPath, targetFrameworkMoniker, configuration);

    var (success, stdout, stderr) = await ProcessUtils.RunProcessAsync(command, args, cancellationToken);

    var (errors, warnings) = ParseBuildOutput(stdout, stderr);

    return new BuildResult(
        success,
        errors,
        warnings
    );
  }

  private class MsBuildPropertiesResponse
  {
    public Dictionary<string, string?> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  }

  private static string GetLanguage(string path) => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? "csharp" :
    path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ? "fsharp" :
    "unknown";

  public async Task<DotnetProjectProperties> GetProjectPropertiesAsync(
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
        "UsingMicrosoftNETSdkWorker", "UsingMicrosoftNETSdkWeb",  "UseIISExpress"
    };

    var (command, args) = GetCommandAndArguments(
        clientService.UseVisualStudio ? MSBuildType.VisualStudio : MSBuildType.SDK,
        projectPath,
        targetFrameworkMoniker,
        configuration);

    args += " -nologo -v:minimal " + string.Join(" ", propsToQuery.Select(p => $"-getProperty:{p}"));

    var (success, stdout, stderr) = await ProcessUtils.RunProcessAsync(command, args, cancellationToken);
    if (!success)
      throw new InvalidOperationException($"Failed to get project properties: {stderr}");

    var msbuildOutput = JsonSerializer.Deserialize<MsBuildPropertiesResponse>(stdout);
    var values = msbuildOutput?.Properties ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    string? TryGet(string name) => values.TryGetValue(name, out var v) ? v : null;
    bool TryGetBool(string name) => values.TryGetValue(name, out var v) && bool.TryParse(v, out var b) && b;

    var tfm = TryGet("TargetFramework");
    var versionMoniker = tfm is { } s && s.StartsWith("net", StringComparison.OrdinalIgnoreCase)
        ? s[3..]
        : tfm;

    var nugetVersion = TryGet("Version");
    var targetFrameworkVersion = TryGet("TargetFrameworkVersion");
    var isNetFramework = targetFrameworkVersion is not null && targetFrameworkVersion.StartsWith("v4");
    var useIISExpress = TryGetBool("UseIISExpress");
    var targetPath = TryGet("TargetPath");
    var projectName = Path.GetFileNameWithoutExtension(projectPath);

    return new DotnetProjectProperties(
        ProjectName: projectName,
        Language: GetLanguage(projectPath),
        IsWebProject: TryGetBool("UsingMicrosoftNETSdkWeb"),
        IsWorkerProject: TryGetBool("UsingMicrosoftNETSdkWorker"),
        OutputPath: TryGet("OutputPath"),
        OutputType: TryGet("OutputType"),
        TargetExt: TryGet("TargetExt"),
        AssemblyName: TryGet("AssemblyName"),
        TargetFramework: tfm,
        TargetFrameworks: TryGet("TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries),
        IsTestProject: TryGetBool("IsTestProject"),
        UserSecretsId: TryGet("UserSecretsId"),
        TestingPlatformDotnetTestSupport: TryGetBool("TestingPlatformDotnetTestSupport"),
        TargetPath: TryGet("TargetPath"),
        GeneratePackageOnBuild: TryGetBool("GeneratePackageOnBuild"),
        IsPackable: TryGetBool("IsPackable"),
        PackageId: TryGet("PackageId"),
        NugetVersion: string.IsNullOrWhiteSpace(nugetVersion) ? null : nugetVersion,
        Version: targetFrameworkVersion,
        PackageOutputPath: TryGet("PackageOutputPath"),
        IsMultiTarget: (TryGet("TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries).Length ?? 0) > 1,
        IsNetFramework: isNetFramework,
        UseIISExpress: useIISExpress,
        RunCommand: BuildRunCommand(!isNetFramework, useIISExpress, targetPath, projectPath, projectName),
        TestCommand: BuildTestCommand(!isNetFramework, targetPath, projectPath),
        BuildCommand: BuildBuildCommand(!isNetFramework, projectPath)
    );
  }

  private string BuildRunCommand(bool isSdk, bool useIISExpress, string? targetPath, string projectPath, string projectName)
  {
      var buildCmd = BuildBuildCommand(isSdk, projectPath);

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

  private string BuildBuildCommand(bool isSdk, string projectPath)
  {
    var normalizedPath = Path.GetFullPath(projectPath)
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    return isSdk
        ? $"dotnet build \"{normalizedPath}\""
        : $"& \"{locator.GetVisualStudioMSBuildPath()}\" \"{normalizedPath}\"";
  }

  private (string Command, string Arguments) GetCommandAndArguments(
      MSBuildType type,
      string targetPath,
      string? targetFrameworkMoniker,
      string configuration)
  {
    var tfmArg = string.IsNullOrWhiteSpace(targetFrameworkMoniker)
        ? string.Empty
        : $" /p:TargetFramework={targetFrameworkMoniker}";

    return type switch
    {
      MSBuildType.SDK => ("dotnet", $"msbuild \"{targetPath}\" /p:Configuration={configuration}{tfmArg}"),
      MSBuildType.VisualStudio => (locator.GetVisualStudioMSBuildPath(), $"\"{targetPath}\" /p:Configuration={configuration}{tfmArg}"),
      _ => throw new InvalidOperationException("Unknown MSBuild type")
    };
  }

  private static (List<BuildMessage> Errors, List<BuildMessage> Warnings) ParseBuildOutput(string stdout, string stderr)
  {
    var regex = MsBuildLoggingLine();

    var messages = stdout
           .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
           .Select(line => regex.Match(line))
           .Where(match => match.Success)
           .Select(match => new BuildMessage(
               Type: match.Groups["type"].Value,
               FilePath: match.Groups["file"].Value,
               LineNumber: int.Parse(match.Groups["line"].Value),
               ColumnNumber: int.Parse(match.Groups["col"].Value),
               Code: match.Groups["code"].Value,
               Message: match.Groups["msg"].Value
           ))
           .ToList();

    var errors = messages.Where(m => m.Type.Equals("error", StringComparison.OrdinalIgnoreCase)).ToList();
    var warnings = messages.Where(m => m.Type.Equals("warning", StringComparison.OrdinalIgnoreCase)).ToList();

    var stderrErrors = stderr
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => new BuildMessage("error", "", 0, 0, "", line));

    errors.AddRange(stderrErrors);

    return (errors, warnings);
  }

  [GeneratedRegex(@"^(?<file>.*)\((?<line>\d+),(?<col>\d+)\): (?<type>error|warning) (?<code>\S+): (?<msg>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
  private static partial Regex MsBuildLoggingLine();
}