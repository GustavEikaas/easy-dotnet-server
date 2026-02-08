using EasyDotnet.BuildServer.Contracts;
using Microsoft.Build.Framework;

namespace EasyDotnet.BuildServer.Models;

public class InMemoryLogger : ILogger
{
  public List<BuildMessage> Errors { get; } = [];
  public List<BuildMessage> Warnings { get; } = [];
  public string? Parameters { get; set; } = "";
  public LoggerVerbosity Verbosity { get; set; }

  public void Initialize(IEventSource eventSource)
  {
    eventSource.ErrorRaised += (s, e) => Errors.Add(new BuildMessage(
        "Error",
        e.File ?? "",
        e.LineNumber,
        e.ColumnNumber,
        e.Code ?? "",
        e.Message ?? "",
        e.ProjectFile
    ));

    eventSource.WarningRaised += (s, e) => Warnings.Add(new BuildMessage(
        "Warning",
        e.File ?? "",
        e.LineNumber,
        e.ColumnNumber,
        e.Code ?? "",
        e.Message ?? "",
        e.ProjectFile
    ));
  }

  public void Shutdown() { }
}