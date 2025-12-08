using System.Text;
using System.Text.RegularExpressions;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.MsBuild;

namespace EasyDotnet.Infrastructure.Dap;

public static class InitializeRequestRewriter
{

  private static readonly Regex MsBuildVarRegex = new(@"\$\(([^)]+)\)", RegexOptions.Compiled);
  private static readonly Regex WindowsEnvVarRegex = new("%([^%]+)%", RegexOptions.Compiled | RegexOptions.IgnoreCase);

  public static Task<InterceptableAttachRequest> CreateInitRequestBasedOnProjectType(DotnetProject project, LaunchProfile? launchProfile, InterceptableAttachRequest request, string cwd, int? processId)
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

  private static Dictionary<string, string> BuildEnvironmentVariables(LaunchProfile? launchProfile) => launchProfile == null
        ? []
        : launchProfile.EnvironmentVariables
            .Concat(
                !string.IsNullOrEmpty(launchProfile.ApplicationUrl)
                    ? [new KeyValuePair<string, string>("ASPNETCORE_URLS", launchProfile.ApplicationUrl)]
                    : Array.Empty<KeyValuePair<string, string>>()
            )
            .ToDictionary(kv => kv.Key, kv => kv.Value);

  private static async Task<InterceptableAttachRequest> CreateLaunchRequestAsync(InterceptableAttachRequest request, DotnetProject project, LaunchProfile? launchProfile, string cwd)
  {

    var env = BuildEnvironmentVariables(launchProfile);
    request.Type = "request";
    request.Arguments.Cwd = cwd;
    request.Command = "launch";
    request.Arguments.Request = "launch";
    request.Arguments.Program = project.TargetPath!;

    if (!string.IsNullOrWhiteSpace(launchProfile?.CommandLineArgs))
    {
      var interpolatedArgs = InterpolateVariables(launchProfile.CommandLineArgs, project);
      request.Arguments.Args = ParseCommandLineArgs(interpolatedArgs);
    }

    request.Arguments.Env =
        (request.Arguments.Env ?? [])
        .Concat(env ?? Enumerable.Empty<KeyValuePair<string, string>>())
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    return await Task.FromResult(request);
  }

  private static async Task<InterceptableAttachRequest> CreateAttachRequestAsync(InterceptableAttachRequest request, int processId, string cwd)
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

    // Replace MSBuild-style variables: $(VarName)
    result = MsBuildVarRegex.Replace(result, match =>
    {
      var varName = match.Groups[1].Value;
      return variables.TryGetValue(varName, out var value) ? value : match.Value;
    });

    // Replace Windows environment variables: %VarName%
    result = WindowsEnvVarRegex.Replace(result, match =>
    {
      var varName = match.Groups[1].Value;
      return Environment.GetEnvironmentVariable(varName) ?? match.Value;
    });

    return result;
  }

  private static string[] ParseCommandLineArgs(string commandLineArgs)
  {
    var args = new List<string>();
    var currentArg = new StringBuilder();
    var inQuotes = false;
    var escapeNext = false;

    for (var i = 0; i < commandLineArgs.Length; i++)
    {
      var c = commandLineArgs[i];

      // Handle escape sequences
      if (escapeNext)
      {
        currentArg.Append(c);
        escapeNext = false;
        continue;
      }

      // Check for escape character before quote
      if (c == '\\' && i + 1 < commandLineArgs.Length && commandLineArgs[i + 1] == '"')
      {
        escapeNext = true;
        continue;
      }

      // Handle quotes
      if (c == '"')
      {
        inQuotes = !inQuotes;
      }
      // Handle spaces (argument separators)
      else if (c == ' ' && !inQuotes)
      {
        if (currentArg.Length > 0)
        {
          args.Add(currentArg.ToString());
          currentArg.Clear();
        }
      }
      // Regular character
      else
      {
        currentArg.Append(c);
      }
    }

    // Add the last argument if any
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
    //Env vars are automatically handled by netcoredbg but $(UserProfile) is special syntax
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
}