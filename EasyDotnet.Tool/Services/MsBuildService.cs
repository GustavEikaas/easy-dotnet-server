using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Locator;

namespace EasyDotnet.Services;

public sealed record BuildMessage(string Type, string FilePath, int LineNumber, int ColumnNumber, string Code, string? Message);
public sealed record SdkInstallation(string Name, string Moniker, Version Version, string MSBuildPath, string VisualStudioRootPath);
public sealed record BuildResult(bool Success, List<BuildMessage> Errors, List<BuildMessage> Warnings);

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