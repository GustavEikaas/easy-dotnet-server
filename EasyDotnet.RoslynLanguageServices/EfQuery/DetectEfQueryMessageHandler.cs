using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Extensions;

namespace EasyDotnet.RoslynLanguageServices.EfQuery;

public sealed class DetectEfQueryMessageHandler
    : IExtensionDocumentMessageHandler<DetectEfQueryRequest, DetectEfQueryResponse>
{
  public async Task<DetectEfQueryResponse> ExecuteAsync(
      DetectEfQueryRequest message,
      ExtensionMessageContext context,
      Document document,
      CancellationToken cancellationToken)
  {
    var root = await document.GetSyntaxRootAsync(cancellationToken);
    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
    var text = await document.GetTextAsync(cancellationToken);

    if (root is null || semanticModel is null || message.Line < 0 || message.Line >= text.Lines.Count)
    {
      return DetectEfQueryResponse.NotFound;
    }

    var line = text.Lines[message.Line];
    var position = Math.Min(line.Start + Math.Max(message.Character, 0), line.End);

    var detection = EfQueryDetector.FindQuery(root, semanticModel, position, cancellationToken);
    if (detection is null)
    {
      return DetectEfQueryResponse.NotFound;
    }

    var start = detection.QueryExpression.GetLocation().GetLineSpan().StartLinePosition;
    return new DetectEfQueryResponse(true, start.Line, start.Character);
  }
}