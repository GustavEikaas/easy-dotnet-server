using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class CompletionHandler(
    IDocumentManager documentManager,
    ICompletionService completionService) : BaseController
{
  [JsonRpcMethod("textDocument/completion", UseSingleObjectParameterDeserialization = true)]
  public async Task<CompletionList> GetCompletion(CompletionParams completionParams, CancellationToken cancellationToken)
  {
    var doc = documentManager.GetDocument(completionParams.TextDocument.Uri);
    if (doc == null)
    {
      return new CompletionList { Items = [] };
    }

    var result = await completionService.GetCompletionsAsync(doc, completionParams.Position.Line, completionParams.Position.Character, cancellationToken);
    return new CompletionList { Items = result.Items, IsIncomplete = result.IsIncomplete };
  }
}