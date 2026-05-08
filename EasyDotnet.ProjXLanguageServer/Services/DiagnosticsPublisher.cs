using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IDiagnosticsPublisher
{
  Task PublishAsync(Uri uri, Diagnostic[] diagnostics);
}

public class DiagnosticsPublisher(JsonRpc jsonRpc) : IDiagnosticsPublisher
{
  public Task PublishAsync(Uri uri, Diagnostic[] diagnostics) =>
      jsonRpc.NotifyWithParameterObjectAsync(
          "textDocument/publishDiagnostics",
          new PublishDiagnosticParams { Uri = uri, Diagnostics = diagnostics });
}