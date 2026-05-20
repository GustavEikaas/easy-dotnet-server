using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Services;

public sealed class LspProgressOptions
{
  public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(200);
}

public interface ILspProgressReporter
{
  Task<T> RunWithDelayedProgressAsync<T>(
      string title,
      string message,
      Func<CancellationToken, Task<T>> operation,
      CancellationToken cancellationToken);
}

public sealed class LspProgressReporter(
    JsonRpc jsonRpc,
    LspProgressOptions options,
    ILogger<LspProgressReporter> logger) : ILspProgressReporter
{
  public async Task<T> RunWithDelayedProgressAsync<T>(
      string title,
      string message,
      Func<CancellationToken, Task<T>> operation,
      CancellationToken cancellationToken)
  {
    var operationTask = operation(cancellationToken);
    if (operationTask.IsCompleted)
    {
      return await operationTask;
    }

    Task delayTask;
    try
    {
      delayTask = Task.Delay(options.Delay, cancellationToken);
      if (await Task.WhenAny(operationTask, delayTask) == operationTask)
      {
        return await operationTask;
      }

      await delayTask;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return await operationTask;
    }

    var token = $"projx-nuget-{Guid.NewGuid():N}";
    var started = false;
    try
    {
      await NotifyAsync(new LspProgressParams(
          token,
          new LspProgressValue("begin", title, message, Percentage: null, Cancellable: false)));
      started = true;

      return await operationTask;
    }
    finally
    {
      if (started)
      {
        await NotifyAsync(new LspProgressParams(
            token,
            new LspProgressValue("end")));
      }
    }
  }

  private async Task NotifyAsync(LspProgressParams progress)
  {
    try
    {
      await jsonRpc.NotifyWithParameterObjectAsync("$/progress", progress);
    }
    catch (Exception e)
    {
      logger.LogDebug(e, "Failed to send LSP progress notification");
    }
  }
}

internal sealed record LspProgressParams(string Token, LspProgressValue Value);

internal sealed record LspProgressValue(
    string Kind,
    string? Title = null,
    string? Message = null,
    int? Percentage = null,
    bool? Cancellable = null);
