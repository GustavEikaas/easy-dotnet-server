using EasyDotnet.BuildServer.Contracts;
using Microsoft.Build.Framework;

namespace EasyDotnet.BuildServer;

public class DiagnosticLogger(List<BuildDiagnostic> diagnostics) : ILogger
{
  public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Normal;
  public string? Parameters { get; set; }

  public void Initialize(IEventSource eventSource)
  {
    eventSource.ErrorRaised += (_, e) => diagnostics.Add(new BuildDiagnostic(
          File: e.File,
          LineNumber: e.LineNumber,
          ColumnNumber: e.ColumnNumber,
          EndLineNumber: e.EndLineNumber,
          EndColumnNumber: e.EndColumnNumber,
          Message: e.Message,
          Code: e.Code,
          ProjectFile: e.ProjectFile,
          Severity: BuildDiagnosticSeverity.Error));

    eventSource.WarningRaised += (_, e) => diagnostics.Add(new BuildDiagnostic(
          File: e.File,
          LineNumber: e.LineNumber,
          ColumnNumber: e.ColumnNumber,
          EndLineNumber: e.EndLineNumber,
          EndColumnNumber: e.EndColumnNumber,
          Message: e.Message,
          Code: e.Code,
          ProjectFile: e.ProjectFile,
          Severity: BuildDiagnosticSeverity.Warning));

    eventSource.MessageRaised += (_, e) =>
    {
      if (e.Importance == MessageImportance.High)
      {
        diagnostics.Add(new BuildDiagnostic(
            File: e.File,
            LineNumber: e.LineNumber,
            ColumnNumber: e.ColumnNumber,
            EndLineNumber: e.EndLineNumber,
            EndColumnNumber: e.EndColumnNumber,
            Message: e.Message,
            Code: e.Code,
            ProjectFile: e.ProjectFile,
            Severity: BuildDiagnosticSeverity.Message));
      }
    };
  }

  public void Shutdown()
  {
  }
}