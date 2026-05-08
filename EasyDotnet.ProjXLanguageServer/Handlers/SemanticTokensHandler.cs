using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class SemanticTokensHandler(
    IDocumentManager documentManager,
    ISemanticTokensService semanticTokensService) : BaseController
{
  [JsonRpcMethod("textDocument/semanticTokens/full", UseSingleObjectParameterDeserialization = true)]
  public SemanticTokens GetSemanticTokens(SemanticTokensParams @params)
  {
    var doc = documentManager.GetDocument(@params.TextDocument.Uri);
    if (doc == null)
    {
      return new SemanticTokens { Data = [] };
    }

    return semanticTokensService.GetTokens(doc);
  }
}