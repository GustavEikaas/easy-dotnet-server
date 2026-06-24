using System.CommandLine.Parsing;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Models.LaunchProfile;

namespace EasyDotnet.IDE.Workspace.Services;

public static class WorkspaceRunCommandBuilder
{
  public static RunCommand Build(
      DotnetProject project,
      LaunchProfile? launchProfile,
      IEnumerable<string>? applicationArguments,
      Dictionary<string, string>? environmentVariables)
  {
    var command = ResolveSdkRunCommand(project);
    ValidateHostCompatibility(project, command.Executable);

    if (launchProfile?.CommandLineArgs is not null)
    {
      command.Arguments.AddRange(LaunchProfileUtils.ParseCommandLineArgs(launchProfile.CommandLineArgs, project));
    }

    if (applicationArguments is not null)
    {
      command.Arguments.AddRange(applicationArguments);
    }

    var env = LaunchProfileUtils.GetEnvironmentVariables(launchProfile);
    if (environmentVariables is not null)
    {
      foreach (var kvp in environmentVariables)
      {
        env[kvp.Key] = kvp.Value;
      }
    }

    AddDotnetRootForAppHost(project, env);

    return new RunCommand(
        command.Executable,
        command.Arguments,
        LaunchProfileUtils.ResolveCwd(launchProfile, project),
        env);
  }

  private static (string Executable, List<string> Arguments) ResolveSdkRunCommand(DotnetProject project)
  {
    if (!string.IsNullOrWhiteSpace(project.RunCommand))
    {
      var args = string.IsNullOrWhiteSpace(project.RunArguments)
          ? []
          : CommandLineParser.SplitCommandLine(project.RunArguments).ToList();

      return (project.RunCommand, args);
    }

    if (string.IsNullOrWhiteSpace(project.TargetPath))
    {
      throw new InvalidOperationException("Project has no target path to run.");
    }

    if (project.UsingGodotNETSdk)
    {
      var runCommand = WellKnownEnvironment.GodotBinPath.GetValueOrDefault("godot");
      return (runCommand, []);
    }

    if (string.Equals(project.TargetFrameworkIdentifier, ".NETCoreApp", StringComparison.OrdinalIgnoreCase)
        && Path.GetExtension(project.TargetPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
    {
      return ("dotnet", ["exec", project.TargetPath]);
    }

    return (project.TargetPath, []);
  }

  private static void AddDotnetRootForAppHost(DotnetProject project, Dictionary<string, string> env)
  {
    if (!project.UseAppHost)
    {
      return;
    }

    var dotnetHostPath = project.DOTNET_HOST_PATH;
    if (string.IsNullOrWhiteSpace(dotnetHostPath))
    {
      return;
    }

    var variableName = GetDotnetRootVariableName(project);
    if (variableName is null || Environment.GetEnvironmentVariable(variableName) is not null)
    {
      return;
    }

    var dotnetRoot = Path.GetDirectoryName(dotnetHostPath);
    if (!string.IsNullOrWhiteSpace(dotnetRoot))
    {
      env[variableName] = dotnetRoot;
    }
  }

  private static void ValidateHostCompatibility(DotnetProject project, string executable)
  {
    if (!project.UseAppHost || OperatingSystem.IsWindows())
    {
      return;
    }

    var rid = string.IsNullOrWhiteSpace(project.RuntimeIdentifier)
        ? project.DefaultAppHostRuntimeIdentifier
        : project.RuntimeIdentifier;

    if (rid?.StartsWith("win-", StringComparison.OrdinalIgnoreCase) == true
        || executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException(
          $"Project produces a Windows apphost for RuntimeIdentifier '{rid}'. Run it on Windows, or set UseAppHost=false for a framework-dependent local run.");
    }
  }

  private static string? GetDotnetRootVariableName(DotnetProject project)
  {
    var rid = string.IsNullOrWhiteSpace(project.RuntimeIdentifier)
        ? project.DefaultAppHostRuntimeIdentifier
        : project.RuntimeIdentifier;

    var arch = TryGetRidArchitecture(rid);
    if (arch is null)
    {
      return Environment.Is64BitProcess ? "DOTNET_ROOT" : "DOTNET_ROOT(x86)";
    }

    return arch switch
    {
      "x64" => "DOTNET_ROOT_X64",
      "x86" => "DOTNET_ROOT_X86",
      "arm64" => "DOTNET_ROOT_ARM64",
      "arm" => "DOTNET_ROOT_ARM",
      _ => null
    };
  }

  private static string? TryGetRidArchitecture(string? runtimeIdentifier)
  {
    if (string.IsNullOrWhiteSpace(runtimeIdentifier))
    {
      return null;
    }

    var archStart = runtimeIdentifier.IndexOf('-') + 1;
    if (archStart <= 0 || archStart >= runtimeIdentifier.Length)
    {
      return null;
    }

    var archEnd = runtimeIdentifier.IndexOf('-', archStart);
    return runtimeIdentifier[archStart..(archEnd > 0 ? archEnd : runtimeIdentifier.Length)].ToLowerInvariant();
  }
}