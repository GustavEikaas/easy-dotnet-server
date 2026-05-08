using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class DefinitionHandler(
    IDocumentManager documentManager,
    IDefinitionService definitionService) : BaseController
{
  [JsonRpcMethod("textDocument/definition", UseSingleObjectParameterDeserialization = true)]
  public Location[] GetDefinition(TextDocumentPositionParams @params)
  {
    var doc = documentManager.GetDocument(@params.TextDocument.Uri);
    if (doc == null)
    {
      return [];
    }

    var location = definitionService.GetDefinition(doc, @params.Position.Line, @params.Position.Character);
    return location == null ? [] : [location];
  }
}