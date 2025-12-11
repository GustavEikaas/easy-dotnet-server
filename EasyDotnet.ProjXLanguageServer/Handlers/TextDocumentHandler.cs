using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class TextDocumentHandler : BaseController
{
  private readonly IDocumentManager _documentManager;
  private readonly IDiagnosticsService _diagnosticsService;
  private readonly JsonRpc _jsonRpc;

  public TextDocumentHandler(JsonRpc jsonRpc, IDocumentManager documentManager, IDiagnosticsService diagnosticsService)
    : base(jsonRpc)
  {
    _documentManager = documentManager;
    _diagnosticsService = diagnosticsService;
    _jsonRpc = jsonRpc;
  }

  [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
  public async Task OnDidOpenTextDocument(DidOpenTextDocumentParams @params)
  {
    _documentManager.OpenDocument(
        @params.TextDocument.Uri,
        @params.TextDocument.Text,
        @params.TextDocument.Version
    );
    await PublishDiagnosticsAsync(@params.TextDocument.Uri);
  }

  [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
  public async Task OnDidChangeTextDocument(DidChangeTextDocumentParams @params)
  {
    if (@params.ContentChanges.Length > 0)
    {
      var change = @params.ContentChanges[^1];
      _documentManager.UpdateDocument(
          @params.TextDocument.Uri,
          change.Text,
          @params.TextDocument.Version
      );
      await PublishDiagnosticsAsync(@params.TextDocument.Uri);
    }
  }

  [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
  public async Task OnDidCloseTextDocument(DidCloseTextDocumentParams @params)
  {
    _documentManager.CloseDocument(@params.TextDocument.Uri);
    await _jsonRpc.NotifyWithParameterObjectAsync("textDocument/publishDiagnostics", new PublishDiagnosticParams
    {
      Uri = @params.TextDocument.Uri,
      Diagnostics = []
    });
  }

  private async Task PublishDiagnosticsAsync(Uri uri)
  {
    var content = _documentManager.GetDocumentContent(uri);
    if (string.IsNullOrEmpty(content))
      return;

    var diagnostics = _diagnosticsService.AnalyzeDocument(content);

    await _jsonRpc.NotifyWithParameterObjectAsync("textDocument/publishDiagnostics", new PublishDiagnosticParams
    {
      Uri = uri,
      Diagnostics = diagnostics
    });
  }
}