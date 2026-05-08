using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class TextDocumentHandler(
    IDocumentManager documentManager,
    IDiagnosticsService diagnosticsService,
    IDiagnosticsPublisher diagnosticsPublisher) : BaseController
{
  [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
  public Task OnDidOpenTextDocument(DidOpenTextDocumentParams @params)
  {
    documentManager.OpenDocument(@params.TextDocument.Uri, @params.TextDocument.Text, @params.TextDocument.Version);
    return PublishAsync(@params.TextDocument.Uri);
  }

  [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
  public Task OnDidChangeTextDocument(DidChangeTextDocumentParams @params)
  {
    if (@params.ContentChanges.Length == 0)
      return Task.CompletedTask;

    var change = @params.ContentChanges[^1];
    documentManager.UpdateDocument(@params.TextDocument.Uri, change.Text, @params.TextDocument.Version);
    return PublishAsync(@params.TextDocument.Uri);
  }

  [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
  public Task OnDidCloseTextDocument(DidCloseTextDocumentParams @params)
  {
    documentManager.CloseDocument(@params.TextDocument.Uri);
    return diagnosticsPublisher.PublishAsync(@params.TextDocument.Uri, []);
  }

  private Task PublishAsync(Uri uri)
  {
    var doc = documentManager.GetDocument(uri);
    if (doc == null)
      return Task.CompletedTask;
    var diagnostics = diagnosticsService.GetDiagnostics(doc);
    return diagnosticsPublisher.PublishAsync(uri, diagnostics);
  }
}
