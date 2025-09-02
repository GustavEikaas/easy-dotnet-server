using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EasyDotnet;

public static class ProcessUtils
{

  public static async Task<(bool Success, string StdOut, string StdErr)> RunProcessAsync(string command, string arguments, CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = command,
      Arguments = arguments,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {command} process.");

    var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

    await Task.WhenAll(stdOutTask, stdErrTask);

    await process.WaitForExitAsync(cancellationToken);

    return (process.ExitCode == 0, stdOutTask.Result, stdErrTask.Result);
  }
}