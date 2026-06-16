using System.Text;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Aspire.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> that formats logs into the <see cref="RingLogState.Sink"/> (for
/// <c>_server/logdump</c>) and also writes to stderr (captured live by the IDE's AspireHostFactory).
/// Level filtering is dynamic via <see cref="RingLogState"/>.
/// </summary>
public sealed class RingLoggerProvider(RingLogState state) : ILoggerProvider
{
  public ILogger CreateLogger(string categoryName) => new RingLogger(categoryName, state);
  public void Dispose() { }

  private sealed class RingLogger(string category, RingLogState state) : ILogger
  {
    public IDisposable? BeginScope<TState>(TState s) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => state.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState st, Exception? exception, Func<TState, Exception?, string> formatter)
    {
      if (!IsEnabled(logLevel)) return;

      var shortCategory = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
      var sb = new StringBuilder();
      sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append(' ').Append(Tag(logLevel))
        .Append("] [Aspire] ").Append(shortCategory).Append(": ").Append(formatter(st, exception));
      if (exception is not null)
      {
        sb.Append(Environment.NewLine).Append(exception);
      }

      var line = sb.ToString();
      Console.Error.WriteLine(line);
      state.Sink.Add(line);
    }

    private static string Tag(LogLevel level) => level switch
    {
      LogLevel.Trace => "TRC",
      LogLevel.Debug => "DBG",
      LogLevel.Information => "INF",
      LogLevel.Warning => "WRN",
      LogLevel.Error => "ERR",
      LogLevel.Critical => "FTL",
      _ => "INF",
    };
  }
}