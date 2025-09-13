using System.Diagnostics;

namespace EasyDotnet.Infrastructure.Process;

/// <summary>
/// Options used to configure the behavior of a process execution.
/// </summary>
/// <param name="KillOnTimeout">
/// If <c>true</c>, the process will be forcibly killed if it exceeds the timeout period.  
/// If <c>false</c>, the process will remain running even after timeout.
/// </param>
/// <param name="CancellationTimeout">
/// Optional timeout for process execution.  
/// If not specified, a default timeout of 2 minutes is applied.
/// </param>
public record ProcessOptions(
    bool KillOnTimeout = false,
    TimeSpan? CancellationTimeout = null
);

public interface IProcessQueue
{
  /// <summary>
  /// Gets the number of available slots in the process queue.
  /// </summary>
  /// <returns>
  /// The number of available slots (<see cref="int"/>) for process execution.
  /// </returns>
  int CurrentCount();
  /// <summary>
  /// Executes a process asynchronously with concurrency control.
  /// </summary>
  /// <param name="command">
  /// The name of the process or executable file to start.
  /// </param>
  /// <param name="arguments">
  /// The command-line arguments to pass to the process.
  /// </param>
  /// <param name="options">
  /// Optional <see cref="ProcessOptions"/> to configure timeout and kill behavior.  
  /// If <c>null</c>, defaults are applied.
  /// </param>
  /// <param name="cancellationToken">
  /// A <see cref="CancellationToken"/> to observe while waiting for the process to complete.
  /// </param>
  /// <returns>
  /// A task that returns a tuple:  
  /// <list type="bullet">
  ///   <item><c>Success</c>: <c>true</c> if the process exits with code 0; otherwise, <c>false</c>.</item>
  ///   <item><c>StdOut</c>: The captured standard output from the process.</item>
  ///   <item><c>StdErr</c>: The captured standard error output from the process.</item>
  /// </list>
  /// </returns>
  Task<(bool Success, string StdOut, string StdErr)> RunProcessAsync(string command, string arguments, ProcessOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides a queue-based implementation for running external processes with concurrency limits.
/// </summary>
public class ProcessQueue(int maxConcurrent = 35) : IProcessQueue
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
      // logService.Info($"[{DateTime.UtcNow:O}] Request queued for {command} {arguments}");
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

      using var process = System.Diagnostics.Process.Start(startInfo)
          ?? throw new InvalidOperationException($"Failed to start {command} process.");

      try
      {
        var stdOutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        var stdErrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

        await Task.WhenAll(stdOutTask, stdErrTask);

        // if (logService.SourceLevel == SourceLevels.Verbose)
        // {
        //   logService.Info("STDOUT: " + stdOutTask.Result);
        //   logService.Info("STDERR: " + stdErrTask.Result);
        // }

        await process.WaitForExitAsync(linkedCts.Token);

        return (process.ExitCode == 0, stdOutTask.Result, stdErrTask.Result);
      }
      catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && options?.KillOnTimeout != false)
      {
        try
        {
          if (!process.HasExited)
          {

            // logService.Info("[Timeout] Force killing: " + process.ProcessName);
            process.Kill(entireProcessTree: true);
          }
        }
        catch
        {

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