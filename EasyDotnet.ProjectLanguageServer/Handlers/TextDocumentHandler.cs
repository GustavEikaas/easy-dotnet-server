using EasyDotnet.ProjectLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjectLanguageServer.Handlers;

public class TextDocumentHandler(JsonRpc jsonRpc, IDocumentManager documentManager) : BaseController(jsonRpc)
{
  [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
  public void OnDidOpenTextDocument(DidOpenTextDocumentParams @params) => documentManager.OpenDocument(
        @params.TextDocument.Uri,
        @params.TextDocument.Text,
        @params.TextDocument.Version
    );

  [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
  public void OnDidChangeTextDocument(DidChangeTextDocumentParams @params)
  {
    if (@params.ContentChanges.Length > 0)
    {
      var change = @params.ContentChanges[^1];
      documentManager.UpdateDocument(
          @params.TextDocument.Uri,
          change.Text,
          @params.TextDocument.Version
      );
    }
  }

  [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
  public void OnDidCloseTextDocument(DidCloseTextDocumentParams @params) => documentManager.CloseDocument(@params.TextDocument.Uri);
}
