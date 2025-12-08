using System.Text;
using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.MsBuild;

namespace EasyDotnet.Infrastructure.Dap;

public static partial class InitializeRequestRewriter
{
  private static readonly Regex MsBuildVarRegex = MsBuildVar();

  public static Task<InterceptableAttachRequest> CreateInitRequestBasedOnProjectType(
    DotnetProject project,
    LaunchProfile? launchProfile,
    InterceptableAttachRequest request,
    string cwd,
    int? processId)
  {
    if (project.IsTestProject && !project.TestingPlatformDotnetTestSupport && processId is not null)
    {
      return CreateAttachRequestAsync(request, processId.Value, cwd);
    }
    else
    {
      return CreateLaunchRequestAsync(request, project, launchProfile, cwd);
    }
  }

  private static Dictionary<string, string> BuildEnvironmentVariables(
    LaunchProfile? launchProfile,
    DotnetProject project)
  {
    if (launchProfile == null)
      return [];

    var env = new Dictionary<string, string>();

    // Interpolate environment variables from launch profile
    foreach (var kvp in launchProfile.EnvironmentVariables)
    {
      var interpolated = InterpolateVariables(kvp.Value, project);
      if (!string.IsNullOrWhiteSpace(interpolated))
        env[kvp.Key] = interpolated;
    }

    // Add ASPNETCORE_URLS if ApplicationUrl is specified
    if (!string.IsNullOrEmpty(launchProfile.ApplicationUrl))
    {
      env["ASPNETCORE_URLS"] = InterpolateVariables(launchProfile.ApplicationUrl, project);
    }

    return env;
  }

  private static async Task<InterceptableAttachRequest> CreateLaunchRequestAsync(
    InterceptableAttachRequest request,
    DotnetProject project,
    LaunchProfile? launchProfile,
    string cwd)
  {
    var env = BuildEnvironmentVariables(launchProfile, project);
    request.Type = "request";
    request.Arguments.Cwd = !string.IsNullOrWhiteSpace(launchProfile?.WorkingDirectory)
        ? InterpolateVariables(launchProfile.WorkingDirectory, project)
        : cwd;
    request.Command = "launch";
    request.Arguments.Request = "launch";
    request.Arguments.Program = project.TargetPath;

    if (!string.IsNullOrWhiteSpace(launchProfile?.CommandLineArgs))
    {
      var interpolatedArgs = InterpolateVariables(launchProfile.CommandLineArgs, project);
      request.Arguments.Args = SplitCommandLineArgs(interpolatedArgs);
    }

    request.Arguments.Env =
        (request.Arguments.Env ?? [])
        .Concat(env)
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    return await Task.FromResult(request);
  }

  private static async Task<InterceptableAttachRequest> CreateAttachRequestAsync(
    InterceptableAttachRequest request,
    int processId,
    string cwd)
  {
    request.Type = "request";
    request.Command = "attach";
    request.Arguments.Request = "attach";
    request.Arguments.ProcessId = processId;
    request.Arguments.Cwd = cwd;

    return await Task.FromResult(request);
  }

  private static string InterpolateVariables(string input, DotnetProject project)
  {
    if (string.IsNullOrWhiteSpace(input))
      return input;

    var variables = BuildVariablesDictionary(project);
    var result = input;

    return MsBuildVarRegex.Replace(result, match =>
    {
      var varName = match.Groups[1].Value;
      return variables.TryGetValue(varName, out var value) ? value : match.Value;
    });
  }

  private static string[] SplitCommandLineArgs(string commandLineArgs)
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
        // Don't include the quote character itself
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

  private static Dictionary<string, string> BuildVariablesDictionary(DotnetProject project)
  {
    var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    AddIfNotNull(variables, "ProjectDir", project.ProjectDir);
    AddIfNotNull(variables, "OutDir", project.OutDir);
    AddIfNotNull(variables, "OutputPath", project.OutputPath);
    AddIfNotNull(variables, "Configuration", project.Configuration);
    AddIfNotNull(variables, "TargetDir", project.TargetDir);
    AddIfNotNull(variables, "TargetName", project.TargetName);
    AddIfNotNull(variables, "TargetFileName", project.TargetFileName);
    AddIfNotNull(variables, "TargetPath", project.TargetPath);
    AddIfNotNull(variables, "AssemblyName", project.AssemblyName);
    AddIfNotNull(variables, "ProjectName", project.ProjectName);
    AddIfNotNull(variables, "TargetFramework", project.TargetFramework);

    // Env vars are automatically handled by netcoredbg but $(UserProfile) is special MSBuild syntax
    AddIfNotNull(variables, "UserProfile", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    return variables;
  }

  private static void AddIfNotNull(Dictionary<string, string> dict, string key, string? value)
  {
    if (!string.IsNullOrWhiteSpace(value))
    {
      dict[key] = value;
    }
  }

  [GeneratedRegex(@"\$\(([^)]+)\)", RegexOptions.Compiled)]
  private static partial Regex MsBuildVar();
}