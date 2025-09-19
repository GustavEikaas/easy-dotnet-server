using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EasyDotnet.Controllers.LaunchProfile;

namespace EasyDotnet.Services.NetCoreDbg;

public static class InitializeRequestRewriter
{

  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  private static int StartTestProcess(string projectPath)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = "dotnet",
      Arguments = $"test \"{projectPath}\" --environment VSTEST_HOST_DEBUG=1",
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    startInfo.EnvironmentVariables["VSTEST_HOST_DEBUG"] = "1";

    using var process = new Process { StartInfo = startInfo };

    var processId = 0;

    var outputReceived = new TaskCompletionSource<bool>();

    process.OutputDataReceived += (sender, e) =>
    {
      if (string.IsNullOrEmpty(e.Data))
        return;

      // Look for "Process Id: 12345"
      var match = System.Text.RegularExpressions.Regex.Match(e.Data, @"Process Id:\s*(\d+)");
      if (match.Success && int.TryParse(match.Groups[1].Value, out var pid))
      {
        processId = pid;
        outputReceived.TrySetResult(true);
      }
    };

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    // Wait until the process ID is captured
    if (!outputReceived.Task.Wait(TimeSpan.FromSeconds(10)))
    {
      throw new InvalidOperationException("Failed to start test process or retrieve process ID.");
    }

    return processId;
  }

  public static Task<string> CreateInitRequestBasedOnProjectType(string projectPath, DotnetProjectProperties project, LaunchProfile? launchProfile, string cwd, int seq)
  {
    if (project.IsTestProject && project.TestingPlatformDotnetTestSupport != true)
    {
      var processId = StartTestProcess(projectPath);
      return CreateAttachRequestAsync(processId, cwd, seq);
    }
    else
    {
      return CreateLaunchRequestAsync(project, launchProfile, cwd, seq);
    }
  }

  public record LaunchRequestArguments(
      string Cwd,
      string Program,
      string Request = "launch",
      string Type = "coreclr",
      Dictionary<string, string>? Env = null
  );

  public record LaunchRequest(
      string Type = "request",
      int Seq = 0,
      string Command = "launch",
      LaunchRequestArguments Arguments = null!
  );


  private static Dictionary<string, string> BuildEnvironmentVariables(LaunchProfile? launchProfile) => launchProfile == null
        ? []
        : launchProfile.EnvironmentVariables
            .Concat(
                !string.IsNullOrEmpty(launchProfile.ApplicationUrl)
                    ? [new KeyValuePair<string, string>("ASPNETCORE_URLS", launchProfile.ApplicationUrl)]
                    : Array.Empty<KeyValuePair<string, string>>()
            )
            .ToDictionary(kv => kv.Key, kv => kv.Value);

  private static async Task<string> CreateLaunchRequestAsync(DotnetProjectProperties project, LaunchProfile? launchProfile, string cwd, int seq)
  {
    var env = BuildEnvironmentVariables(launchProfile);
    var request = new LaunchRequest("request", seq, "launch", new LaunchRequestArguments(cwd, project.TargetPath!, "launch", "coreclr", env));

    return await Task.FromResult(JsonSerializer.Serialize(request, SerializerOptions));
  }

  public record AttachRequestArguments(
      string Cwd,
      int ProcessId,
      string Request = "attach",
      string Type = "coreclr"
  );

  public record AttachRequest(
      string Type = "request",
      int Seq = 0,
      string Command = "attach",
      AttachRequestArguments Arguments = null!
  );

  private static async Task<string> CreateAttachRequestAsync(int processId, string cwd, int seq)
  {
    var request = new AttachRequest("request", seq, "attach", new AttachRequestArguments(cwd, processId, "attach", "coreclr"));

    return await Task.FromResult(JsonSerializer.Serialize(request, SerializerOptions));
  }
}