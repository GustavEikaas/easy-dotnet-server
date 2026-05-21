using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class CodeActionHandler(
    IDocumentManager documentManager,
    ICodeActionService codeActionService) : BaseController
{
  [JsonRpcMethod("textDocument/codeAction", UseSingleObjectParameterDeserialization = true)]
  public CodeAction[] GetCodeActions(CodeActionParams @params)
  {
    var doc = documentManager.GetDocument(@params.TextDocument.Uri);
    if (doc == null)
      return [];
    return codeActionService.GetCodeActions(doc, @params.Range, @params.Context?.Diagnostics ?? []);
  }
}