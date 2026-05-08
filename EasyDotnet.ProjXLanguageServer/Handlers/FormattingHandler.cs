using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class FormattingHandler(
    IDocumentManager documentManager,
    IFormattingService formattingService) : BaseController
{
  [JsonRpcMethod("textDocument/formatting", UseSingleObjectParameterDeserialization = true)]
  public TextEdit[] GetFormatting(DocumentFormattingParams @params)
  {
    var doc = documentManager.GetDocument(@params.TextDocument.Uri);
    if (doc == null)
      return [];
    return formattingService.Format(doc, @params.Options);
  }
}