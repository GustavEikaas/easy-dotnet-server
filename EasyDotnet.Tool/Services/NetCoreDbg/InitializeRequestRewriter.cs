using System;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace EasyDotnet.Services.NetCoreDbg;

public static class InitializeRequestRewriter
{

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

  public static Task<JsonNode> CreateInitRequestBasedOnProjectType(string projectPath, DotnetProjectProperties project, string cwd, int seq)
  {
    if (project.IsTestProject)
    {
      var processId = StartTestProcess(projectPath);
      return CreateAttachRequestAsync(processId, cwd, seq);

    }
    else
    {
      return CreateLaunchRequestAsync(project, cwd, seq);
    }

  }

  private static async Task<JsonNode> CreateLaunchRequestAsync(DotnetProjectProperties project, string cwd, int seq)
  {

    //TODO: resolve project properties launch profile
    var env = new JsonObject
    {
      ["ASPNETCORE_ENVIRONMENT"] = "Development",
      ["ASPNETCORE_URLS"] = "https://localhost:7081;http://localhost:5163"
    };

    var request = new JsonObject
    {
      ["type"] = "request",
      ["seq"] = seq,
      ["command"] = "launch",
      ["arguments"] = new JsonObject
      {
        ["cwd"] = cwd,
        ["env"] = env,
        // ["name"] = "Program",
        ["program"] = project.TargetPath,
        ["request"] = "launch",
        ["type"] = "coreclr"
      }
    };

    return await Task.FromResult(request);
  }


  private static async Task<JsonNode> CreateAttachRequestAsync(int processId, string cwd, int seq)
  {
    var request = new JsonObject
    {
      ["type"] = "request",
      ["seq"] = seq,
      ["command"] = "attach",
      ["arguments"] = new JsonObject
      {
        ["cwd"] = cwd,
        ["processId"] = processId,
        ["request"] = "attach",
        ["type"] = "coreclr"
      }
    };

    return await Task.FromResult(request);
  }
}