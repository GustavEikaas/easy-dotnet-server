using System.Diagnostics;
using StreamJsonRpc;

namespace EasyDotnet.Services;

public class LogService(SourceLevels sourceLevel, JsonRpc server)
{
  public readonly SourceLevels SourceLevel = sourceLevel;

  public void Info(string message) => server.TraceSource.TraceEvent(TraceEventType.Information, 0, message);

  public void Warning(string message) => server.TraceSource.TraceEvent(TraceEventType.Warning, 0, message);

  public void Error(string message) => server.TraceSource.TraceEvent(TraceEventType.Error, 0, message);

  public void Verbose(string message) => server.TraceSource.TraceEvent(TraceEventType.Verbose, 0, message);
}