using System.Diagnostics;
using NuGet.Versioning;

namespace EasyDotnet.IDE.Services;

public sealed record RoslynToolStatus(
    bool IsInstalled,
    string PackageId,
    string CommandName,
    string MinimumRecommendedVersion,
    string? Version,
    string? ExecutablePath,
    bool IsBelowRecommended,
    string? Message = null);

public sealed record RoslynToolCommandResult(bool Success, int ExitCode, string Output);

public static class RoslynToolService
{
  public const string PackageId = "roslyn-language-server";
  public const string CommandName = "roslyn-language-server";
  public const string MinimumRecommendedVersion = "5.8.0-1.26262.10";

  public static async Task<RoslynToolStatus> GetStatusAsync(CancellationToken cancellationToken)
  {
    var executable = ResolveExecutablePath();
    if (executable is null)
    {
      return new RoslynToolStatus(
          IsInstalled: false,
          PackageId,
          CommandName,
          MinimumRecommendedVersion,
          Version: null,
          ExecutablePath: null,
          IsBelowRecommended: false,
          Message: $"{PackageId} is not installed");
    }

    var version = await GetVersionAsync(executable, cancellationToken);
    var isBelowRecommended = IsBelowRecommendedVersion(version);

    return new RoslynToolStatus(
        IsInstalled: true,
        PackageId,
        CommandName,
        MinimumRecommendedVersion,
        Version: version,
        ExecutablePath: executable,
        IsBelowRecommended: isBelowRecommended,
        Message: isBelowRecommended
          ? $"{PackageId} {version} is below the recommended version {MinimumRecommendedVersion}"
          : null);
  }

  public static async Task<RoslynToolStatus> EnsureInstalledAsync(bool autoInstall, CancellationToken cancellationToken)
  {
    var status = await GetStatusAsync(cancellationToken);
    if (status.IsInstalled || !autoInstall)
    {
      return status;
    }

    var install = await InstallAsync(cancellationToken);
    if (!install.Success)
    {
      return status with { Message = install.Output };
    }

    return await GetStatusAsync(cancellationToken);
  }

  public static Task<RoslynToolCommandResult> InstallAsync(CancellationToken cancellationToken) =>
    RunDotnetToolCommandAsync("install", cancellationToken);

  public static Task<RoslynToolCommandResult> UpdateAsync(CancellationToken cancellationToken) =>
    RunDotnetToolCommandAsync("update", cancellationToken);

  public static NuGetVersion? TryParseVersion(string? rawVersion)
  {
    if (string.IsNullOrWhiteSpace(rawVersion))
    {
      return null;
    }

    var firstToken = rawVersion.Trim().Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    if (firstToken is null)
    {
      return null;
    }

    var withoutBuildMetadata = firstToken.Split('+', 2)[0];
    return NuGetVersion.TryParse(withoutBuildMetadata, out var version) ? version : null;
  }

  public static bool IsBelowRecommendedVersion(string? rawVersion)
  {
    var current = TryParseVersion(rawVersion);
    var minimum = NuGetVersion.Parse(MinimumRecommendedVersion);
    return current is not null && current < minimum;
  }

  private static async Task<string?> GetVersionAsync(string executable, CancellationToken cancellationToken)
  {
    var result = await RunProcessAsync(executable, ["--version"], cancellationToken);
    if (result.ExitCode != 0)
    {
      return null;
    }

    return result.Output.Trim();
  }

  private static Task<RoslynToolCommandResult> RunDotnetToolCommandAsync(string action, CancellationToken cancellationToken) =>
    RunProcessAsync("dotnet", ["tool", action, "--global", PackageId, "--prerelease"], cancellationToken);

  private static async Task<RoslynToolCommandResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo(fileName)
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    foreach (var arg in arguments)
    {
      startInfo.ArgumentList.Add(arg);
    }

    using var process = new Process { StartInfo = startInfo };
    process.Start();

    var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

    await process.WaitForExitAsync(cancellationToken);

    var output = string.Join(
        Environment.NewLine,
        new[] { await stdout, await stderr }.Where(x => !string.IsNullOrWhiteSpace(x)));

    return new RoslynToolCommandResult(process.ExitCode == 0, process.ExitCode, output.Trim());
  }

  private static string? ResolveExecutablePath()
  {
    var executableName = OperatingSystem.IsWindows() ? $"{CommandName}.exe" : CommandName;

    var globalTool = GetGlobalToolPath(executableName);
    if (File.Exists(globalTool))
    {
      return globalTool;
    }

    var path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(path))
    {
      return null;
    }

    foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
      var candidate = Path.Combine(dir, executableName);
      if (File.Exists(candidate))
      {
        return candidate;
      }
    }

    return null;
  }

  private static string GetGlobalToolPath(string executableName)
  {
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".dotnet", "tools", executableName);
  }
}