using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class CodeActionHandler(
    IDocumentManager documentManager,
    ICodeActionService codeActionService) : BaseController
{
  [JsonRpcMethod("textDocument/codeAction", UseSingleObjectParameterDeserialization = true)]
  public async Task<CodeAction[]> GetCodeActions(CodeActionParams @params, CancellationToken cancellationToken)
  {
    var doc = documentManager.GetDocument(@params.TextDocument.Uri);
    if (doc == null)
      return [];
    return await codeActionService.GetCodeActionsAsync(
        doc,
        @params.Range,
        @params.Context?.Diagnostics ?? [],
        cancellationToken);
  }
}
