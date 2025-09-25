using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EasyDotnet;

public class JsonRpcLogger(ILogger logger, string who) : TraceListener
{
  public override void Write(string? message)
  {
    if (!string.IsNullOrWhiteSpace(message))
      logger.LogInformation("[{who}]: {Message}", who, message);
  }

  public override void WriteLine(string? message) => Write(message);

  public override void TraceEvent(TraceEventCache? eventCache, string? source, TraceEventType eventType, int id, string? message)
  {
    if (string.IsNullOrWhiteSpace(message)) return;

    switch (eventType)
    {
      case TraceEventType.Critical:
      case TraceEventType.Error:
        logger.LogError("[{who}]: {Source}: {Message}", who, source, message);
        break;
      case TraceEventType.Warning:
        logger.LogWarning("[{who}]:{Source}: {Message}", who, source, message);
        break;
      case TraceEventType.Information:
        logger.LogInformation("[{who}]:{Source}: {Message}", who, source, message);
        break;
      case TraceEventType.Verbose:
        logger.LogDebug("[{who}]:{Source}: {Message}", who, source, message);
        break;
      default:
        logger.LogTrace("[{who}]:{Source}: {Message}", who, source, message);
        break;
    }
  }
}