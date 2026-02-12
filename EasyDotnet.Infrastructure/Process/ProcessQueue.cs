using System.Diagnostics;
using EasyDotnet.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Infrastructure.Process;

/// <summary>
/// Provides a queue-based implementation for running external processes with concurrency limits.
/// </summary>
public class ProcessQueue(int maxConcurrent = 35, ILogger<ProcessQueue>? logger = null) : IProcessQueue
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
      logger?.LogInformation(
          "[{UtcNow}] Request queued for {Command} {Arguments}",
          DateTime.UtcNow.ToString("O"),
          command,
          arguments
      );
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

      logger?.LogInformation("[Command]: {Command} {Arguments}", command, arguments);

      using var process = System.Diagnostics.Process.Start(startInfo)
          ?? throw new InvalidOperationException($"Failed to start {command} process.");

      try
      {
        var stdOutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        var stdErrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

        await Task.WhenAll(stdOutTask, stdErrTask);

        logger?.LogDebug("STDOUT: {Stdout}", stdOutTask.Result);
        logger?.LogDebug("STDERR: {Stderr}", stdErrTask.Result);

        await process.WaitForExitAsync(linkedCts.Token);

        logger?.LogDebug("Process {Command} exited with code {ExitCode}", command, process.ExitCode);

        return (process.ExitCode == 0, stdOutTask.Result, stdErrTask.Result);
      }
      catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && options?.KillOnTimeout != false)
      {
        try
        {
          if (!process.HasExited)
          {
            logger?.LogWarning("Process {Command} timed out, killing process tree...", command);
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