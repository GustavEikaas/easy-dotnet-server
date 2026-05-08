using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class CompletionHandler(
    IDocumentManager documentManager,
    ICompletionService completionService) : BaseController
{
  [JsonRpcMethod("textDocument/completion", UseSingleObjectParameterDeserialization = true)]
  public CompletionList GetCompletion(CompletionParams completionParams)
  {
    var doc = documentManager.GetDocument(completionParams.TextDocument.Uri);
    if (doc == null)
    {
      return new CompletionList { Items = [] };
    }

    var items = completionService.GetCompletions(doc, completionParams.Position.Line, completionParams.Position.Character);
    return new CompletionList { Items = items };
  }
}