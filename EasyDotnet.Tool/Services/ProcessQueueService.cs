using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EasyDotnet.Services;

public record ProcessOptions(
    bool KillOnTimeout = false,
    TimeSpan? CancellationTimeout = null
);

<<<<<<< HEAD
public class ProcessQueueService(LogService logService, int maxConcurrent = 40)
=======
public class ProcessQueueService(LogService logService, int maxConcurrent = 35)
>>>>>>> ddfb09a095de6f259ed1f241bf0fcc858e2919ce
{
  private readonly SemaphoreSlim _semaphore = new(maxConcurrent, maxConcurrent);
  private readonly TimeSpan _maxTimeout = TimeSpan.FromMinutes(2);

  public int CurrentCount() => _semaphore.CurrentCount;

  public async Task<(bool Success, string StdOut, string StdErr)> RunProcessAsync(
      string command,
      string arguments,
      ProcessOptions? options = null,
      CancellationToken cancellationToken = default)
  {
    if (_semaphore.CurrentCount == 0)
    {
      logService.Info($"[{DateTime.UtcNow:O}] Request queued for {command} {arguments}");
    }

    await _semaphore.WaitAsync(cancellationToken);

    try
    {
      var effectiveTimeout = options?.CancellationTimeout ?? _maxTimeout;

      using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

      var startInfo = new ProcessStartInfo
      {
        FileName = command,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = Process.Start(startInfo)
          ?? throw new InvalidOperationException($"Failed to start {command} process.");

      try
      {
        var stdOutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        var stdErrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

        await Task.WhenAll(stdOutTask, stdErrTask);

        await process.WaitForExitAsync(linkedCts.Token);

        return (process.ExitCode == 0, stdOutTask.Result, stdErrTask.Result);
      }
      catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && options?.KillOnTimeout != false)
      {
        try
        {
          if (!process.HasExited)
          {
            process.Kill(entireProcessTree: true);
          }
        }
        catch
        {
<<<<<<< HEAD
          // Swallow errors from Kill (e.g., process already exited)
=======

>>>>>>> ddfb09a095de6f259ed1f241bf0fcc858e2919ce
        }

        throw; // rethrow cancellation so caller knows it was cancelled
      }
    }
    finally
    {
      _semaphore.Release();
    }
  }
}