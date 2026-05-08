using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class SignatureHelpHandler(
    IDocumentManager documentManager,
    ISignatureHelpService signatureHelpService) : BaseController
{
  [JsonRpcMethod("textDocument/signatureHelp", UseSingleObjectParameterDeserialization = true)]
  public SignatureHelp? GetSignatureHelp(SignatureHelpParams @params)
  {
    var doc = documentManager.GetDocument(@params.TextDocument.Uri);
    if (doc == null)
      return null;
    return signatureHelpService.GetSignatureHelp(doc, @params.Position.Line, @params.Position.Character);
  }
}