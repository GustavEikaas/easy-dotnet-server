using System.Text;
using System.Text.RegularExpressions;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Domain.Models.LaunchProfile;

namespace EasyDotnet.Infrastructure;

public static partial class LaunchProfileUtils
{
  /// <summary>
  /// Builds the environment variable dictionary for launching a project from a launch profile.
  /// Handles ASPNETCORE_URLS from ApplicationUrl, and injects ASPNETCORE_ENVIRONMENT=Development
  /// as a fallback if not already set by the profile — matching VS/Rider behaviour.
  /// </summary>
  public static Dictionary<string, string> GetEnvironmentVariables(LaunchProfile? launchProfile)
  {
    if (launchProfile == null)
    {
      return [];
    }

    var env = new Dictionary<string, string>();
    foreach (var kvp in launchProfile.EnvironmentVariables)
    {
      if (!string.IsNullOrWhiteSpace(kvp.Value))
      {
        env[kvp.Key] = kvp.Value;
      }
    }

    if (!string.IsNullOrEmpty(launchProfile.ApplicationUrl))
    {
      env["ASPNETCORE_URLS"] = launchProfile.ApplicationUrl;
    }

    env.TryAdd("ASPNETCORE_ENVIRONMENT", "Development");
    return env;
  }

  public static string ResolveCwd(LaunchProfile? profile, MsBuild.DotnetProject project)
  {
    if (!string.IsNullOrWhiteSpace(profile?.WorkingDirectory))
    {
      return NormalizePath(InterpolateVariables(profile.WorkingDirectory, project));
    }

    if (!string.IsNullOrWhiteSpace(project.RunWorkingDirectory))
    {
      return NormalizePath(project.RunWorkingDirectory);
    }

    return NormalizePath(
        Path.GetDirectoryName(project.TargetPath) ?? project.ProjectDir!);
  }

  public static string[] ParseCommandLineArgs(string? input, DotnetProject project)
  {
    if (string.IsNullOrWhiteSpace(input))
    {
      return [];
    }

    return SplitCommandLineArgs(InterpolateVariables(input, project));
  }

  public static string[] ParseCommandLineArgs(string? input, MsBuild.DotnetProject project)
  {
    if (string.IsNullOrWhiteSpace(input))
    {
      return [];
    }

    return SplitCommandLineArgs(InterpolateVariables(input, project));
  }

  /// <summary>
  /// Resolves the working directory for launching a project, matching VS behaviour:
  /// 1. Explicit WorkingDirectory in the launch profile always wins.
  /// 2. RunWorkingDirectory MSBuild property (set by Web SDK to project dir, empty for console).
  /// 3. Output directory (bin/Debug/...) — console app default.
  /// See dotnet/project-system ProjectAndExecutableLaunchHandlerHelpers.GetDefaultWorkingDirectoryAsync.
  /// </summary>
  public static string ResolveCwd(LaunchProfile? profile, DotnetProject project)
  {
    if (!string.IsNullOrWhiteSpace(profile?.WorkingDirectory))
    {
      return NormalizePath(InterpolateVariables(profile.WorkingDirectory, project));
    }

    if (!string.IsNullOrWhiteSpace(project.RunWorkingDirectory))
    {
      return NormalizePath(project.RunWorkingDirectory);
    }

    return NormalizePath(
        Path.GetDirectoryName(project.TargetPath) ?? project.ProjectDir!);
  }

  public static string InterpolateVariables(string input, MsBuild.DotnetProject project)
  {
    if (string.IsNullOrWhiteSpace(input))
    {
      return input;
    }

    var variables = BuildVariablesDictionary(project);

    return MsBuildVarRegex().Replace(input, match =>
    {
      var varName = match.Groups[1].Value;
      return variables.TryGetValue(varName, out var value) ? value : match.Value;
    });
  }

  public static string InterpolateVariables(string input, DotnetProject project)
  {
    if (string.IsNullOrWhiteSpace(input))
    {
      return input;
    }

    var variables = BuildVariablesDictionary(project);

    return MsBuildVarRegex().Replace(input, match =>
    {
      var varName = match.Groups[1].Value;
      return variables.TryGetValue(varName, out var value) ? value : match.Value;
    });
  }
  /// <summary>
  /// Normalizes path separators to forward slashes for Neovim/DAP consumption.
  /// </summary>
  public static string NormalizePath(string path)
      => string.IsNullOrWhiteSpace(path) ? path : path.Replace('\\', '/');

  public static string[] SplitCommandLineArgs(string commandLineArgs)
  {
    if (string.IsNullOrWhiteSpace(commandLineArgs))
    {
      return [];
    }

    var args = new List<string>();
    var currentArg = new StringBuilder();
    var inQuotes = false;

    foreach (var c in commandLineArgs)
    {
      if (c == '"')
      {
        inQuotes = !inQuotes;
      }
      else if (c == ' ' && !inQuotes)
      {
        if (currentArg.Length > 0)
        {
          args.Add(currentArg.ToString());
          currentArg.Clear();
        }
      }
      else
      {
        currentArg.Append(c);
      }
    }
    if (currentArg.Length > 0)
    {
      args.Add(currentArg.ToString());
    }

    return [.. args];
  }

  private static Dictionary<string, string> BuildVariablesDictionary(MsBuild.DotnetProject project)
  {
    var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    void Add(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) { variables[key] = value; } }

    Add("ProjectDir", project.ProjectDir);
    Add("OutDir", project.OutDir);
    Add("OutputPath", project.OutputPath);
    Add("Configuration", project.Configuration);
    Add("TargetDir", project.TargetDir);
    Add("TargetName", project.TargetName);
    Add("TargetFileName", project.TargetFileName);
    Add("TargetPath", project.TargetPath);
    Add("AssemblyName", project.AssemblyName);
    Add("ProjectName", project.ProjectName);
    Add("TargetFramework", project.TargetFramework);
    Add("UserProfile", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    return variables;
  }

  private static Dictionary<string, string> BuildVariablesDictionary(DotnetProject project)
  {
    var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    void Add(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) { variables[key] = value; } }

    Add("ProjectDir", project.ProjectDir);
    Add("OutDir", project.OutDir);
    Add("OutputPath", project.OutputPath);
    Add("Configuration", project.Configuration);
    Add("TargetDir", project.TargetDir);
    Add("TargetName", project.TargetName);
    Add("TargetFileName", project.TargetFileName);
    Add("TargetPath", project.TargetPath);
    Add("AssemblyName", project.AssemblyName);
    Add("ProjectName", project.ProjectName);
    Add("TargetFramework", project.TargetFramework);
    Add("UserProfile", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    return variables;
  }
  [GeneratedRegex(@"\$\(([^)]+)\)", RegexOptions.Compiled)]
  private static partial Regex MsBuildVarRegex();
}