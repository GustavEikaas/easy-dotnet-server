using EasyDotnet.ProjXLanguageServer.Protocol;
using EasyDotnet.ProjXLanguageServer.Services;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class InlineCompletionHandler(
    IDocumentManager documentManager,
    IInlineCompletionService inlineCompletionService) : BaseController
{
  [JsonRpcMethod("textDocument/inlineCompletion", UseSingleObjectParameterDeserialization = true)]
  public async Task<InlineCompletionList> GetInlineCompletion(InlineCompletionParams inlineCompletionParams, CancellationToken cancellationToken)
  {
    var doc = documentManager.GetDocument(inlineCompletionParams.TextDocument.Uri);
    if (doc == null)
    {
      return new InlineCompletionList { Items = [] };
    }

    return await inlineCompletionService.GetInlineCompletionsAsync(
        doc,
        inlineCompletionParams.Position.Line,
        inlineCompletionParams.Position.Character,
        cancellationToken);
  }
}