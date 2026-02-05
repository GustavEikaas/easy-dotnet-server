using System.Diagnostics;

namespace EasyDotnet.Infrastructure.Dap;

public static class VsTestHelper
{
  public static (System.Diagnostics.Process, int) StartTestProcess(string projectPath)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = "dotnet",
      Arguments = $"test \"{projectPath}\" --environment VSTEST_HOST_DEBUG=1 --no-restore --no-build",
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      WorkingDirectory = Path.GetDirectoryName(projectPath),
      UseShellExecute = false,
      CreateNoWindow = true
    };

    startInfo.EnvironmentVariables["VSTEST_HOST_DEBUG"] = "1";

    using var process = new System.Diagnostics.Process { StartInfo = startInfo };

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

    if (!outputReceived.Task.Wait(TimeSpan.FromSeconds(10)))
    {
      throw new InvalidOperationException("Failed to start test process or retrieve process ID.");
    }

    return (process, processId);
  }
}