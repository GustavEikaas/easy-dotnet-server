using EasyDotnet.ProjXLanguageServer.Services;

namespace EasyDotnet.ProjXLanguageServer.Tests.Helpers;

public sealed class FakeLspProgressReporter : ILspProgressReporter
{
  public int Calls { get; private set; }
  public string? LastTitle { get; private set; }
  public string? LastMessage { get; private set; }

  public async Task<T> RunWithDelayedProgressAsync<T>(
      string title,
      string message,
      Func<CancellationToken, Task<T>> operation,
      CancellationToken cancellationToken)
  {
    Calls++;
    LastTitle = title;
    LastMessage = message;
    return await operation(cancellationToken);
  }
}