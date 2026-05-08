using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class HoverHandler(
    IDocumentManager documentManager,
    IHoverService hoverService) : BaseController
{
  [JsonRpcMethod("textDocument/hover", UseSingleObjectParameterDeserialization = true)]
  public Hover? GetHover(TextDocumentPositionParams @params)
  {
    var doc = documentManager.GetDocument(@params.TextDocument.Uri);
    if (doc == null)
    {
      return null;
    }

    return hoverService.GetHover(doc, @params.Position.Line, @params.Position.Character);
  }
}