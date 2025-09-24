using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.Domain.Models.MsBuild.Project;

namespace EasyDotnet.Infrastructure.Dap;

public static class InitializeRequestRewriter
{
  public static Task<DAP.InterceptableAttachRequest> CreateInitRequestBasedOnProjectType(DotnetProject project, LaunchProfile? launchProfile, DAP.InterceptableAttachRequest request, string cwd, int? processId)
  {
    if (project.IsTestProject && project.TestingPlatformDotnetTestSupport != true && processId is not null)
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

  private static async Task<DAP.InterceptableAttachRequest> CreateLaunchRequestAsync(DAP.InterceptableAttachRequest request, DotnetProject project, LaunchProfile? launchProfile, string cwd)
  {
    var env = BuildEnvironmentVariables(launchProfile);
    var updatedRequest = request with
    {
      Type = "request",
      Command = "launch",
      Arguments = request.Arguments with
      {
        Cwd = cwd,
        Request = "launch",
        Program = project.TargetPath!,
        Env = (request.Arguments.Env ?? [])
                   .Concat(env ?? Enumerable.Empty<KeyValuePair<string, string>>())
                   .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
      }
    };

    return await Task.FromResult(request);
  }

  private static Task<DAP.InterceptableAttachRequest> CreateAttachRequestAsync(
      DAP.InterceptableAttachRequest request,
      int processId,
      string cwd)
  {
    var updatedRequest = request with
    {
      Type = "request",
      Command = "attach",
      Arguments = request.Arguments with
      {
        Request = "attach",
        ProcessId = processId,
        Cwd = cwd
      }
    };

    return Task.FromResult(updatedRequest);
  }
}