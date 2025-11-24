using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.MsBuild;

namespace EasyDotnet.Infrastructure.Dap;

public static class InitializeRequestRewriter
{
  public static Task<InterceptableAttachRequest> CreateInitRequestBasedOnProjectType(DotnetProject project, LaunchProfile? launchProfile, InterceptableAttachRequest request, string cwd, int seq, int? processId)
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
}