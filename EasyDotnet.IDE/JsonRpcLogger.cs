using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EasyDotnet;

/// <summary>
/// Routes TraceSource events from a JsonRpc instance into ILogger.
/// Pass a prefix to distinguish sources in logs:
///   new JsonRpcLogger(logger)               → "[JsonRpc] ..."
///   new JsonRpcLogger(logger, "MTP:api.dll") → "[MTP:api.dll] ..."
/// </summary>
public sealed class JsonRpcLogger(ILogger logger, string? prefix = null) : TraceListener
{
  private readonly string _prefix = prefix is not null ? $"[{prefix}] " : string.Empty;

  public override void Write(string? message)
  {
    if (!string.IsNullOrWhiteSpace(message))
      logger.LogInformation("{Prefix}{Message}", _prefix, message);
  }

  public override void WriteLine(string? message) => Write(message);

  public override void TraceEvent(
      TraceEventCache? eventCache,
      string? source,
      TraceEventType eventType,
      int id,
      string? message)
  {
    if (string.IsNullOrWhiteSpace(message)) return;

    switch (eventType)
    {
      case TraceEventType.Critical:
      case TraceEventType.Error:
        logger.LogError("{Prefix}{Source}: {Message}", _prefix, source, message);
        break;
      case TraceEventType.Warning:
        logger.LogWarning("{Prefix}{Source}: {Message}", _prefix, source, message);
        break;
      case TraceEventType.Information:
        logger.LogInformation("{Prefix}{Source}: {Message}", _prefix, source, message);
        break;
      case TraceEventType.Verbose:
        logger.LogDebug("{Prefix}{Source}: {Message}", _prefix, source, message);
        break;
      default:
        logger.LogTrace("{Prefix}{Source}: {Message}", _prefix, source, message);
        break;
    }
  }
}