using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EasyDotnet;

public class JsonRpcLogger(ILogger logger) : TraceListener
{
  public override void Write(string? message)
  {
    if (!string.IsNullOrWhiteSpace(message))
      logger.LogInformation("{Message}", message);
  }

  public override void WriteLine(string? message) => Write(message);

  public override void TraceEvent(TraceEventCache? eventCache, string? source, TraceEventType eventType, int id, string? message)
  {
    if (string.IsNullOrWhiteSpace(message)) return;

    switch (eventType)
    {
      case TraceEventType.Critical:
      case TraceEventType.Error:
        logger.LogError("{Source}: {Message}", source, message);
        break;
      case TraceEventType.Warning:
        logger.LogWarning("{Source}: {Message}", source, message);
        break;
      case TraceEventType.Information:
        logger.LogInformation("{Source}: {Message}", source, message);
        break;
      case TraceEventType.Verbose:
        logger.LogDebug("{Source}: {Message}", source, message);
        break;
      default:
        logger.LogTrace("{Source}: {Message}", source, message);
        break;
    }
  }
}
