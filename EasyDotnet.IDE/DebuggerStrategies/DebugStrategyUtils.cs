using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.MsBuild;

namespace EasyDotnet.IDE.DebuggerStrategies;

public static partial class DebugStrategyUtils
{
  [GeneratedRegex(@"\$\(([^)]+)\)", RegexOptions.Compiled)]
  private static partial Regex MsBuildVarRegex();

  public static string InterpolateVariables(string input, DotnetProject project)
  {
    if (string.IsNullOrWhiteSpace(input)) return input;

    var variables = BuildVariablesDictionary(project);

    return MsBuildVarRegex().Replace(input, match =>
    {
      var varName = match.Groups[1].Value;
      return variables.TryGetValue(varName, out var value) ? value : match.Value;
    });
  }

  public static Dictionary<string, string> GetEnvironmentVariables(LaunchProfile? launchProfile)
  {
    if (launchProfile == null) return [];

    var env = new Dictionary<string, string>();

    foreach (var kvp in launchProfile.EnvironmentVariables)
    {
      if (!string.IsNullOrWhiteSpace(kvp.Value))
        env[kvp.Key] = kvp.Value;
    }

    if (!string.IsNullOrEmpty(launchProfile.ApplicationUrl))
    {
      env["ASPNETCORE_URLS"] = launchProfile.ApplicationUrl;
    }

    return env;
  }

  public static string NormalizePath(string path)
      => string.IsNullOrWhiteSpace(path) ? path : path.Replace('\\', '/');

  public static string[] SplitCommandLineArgs(string commandLineArgs)
  {
    if (string.IsNullOrWhiteSpace(commandLineArgs)) return [];

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
    if (currentArg.Length > 0) args.Add(currentArg.ToString());
    return [.. args];
  }

  private static Dictionary<string, string> BuildVariablesDictionary(DotnetProject project)
  {
    var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    void Add(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) variables[key] = value; }

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
}