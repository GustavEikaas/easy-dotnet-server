using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Extensions;

namespace EasyDotnet.RoslynLanguageServices.CreateType;

public sealed class CreateTypeFromUsageMessageHandler
    : IExtensionDocumentMessageHandler<CreateTypeFromUsageRequest, CreateTypeFromUsageResponse>
{
  public Task<CreateTypeFromUsageResponse> ExecuteAsync(
      CreateTypeFromUsageRequest message,
      ExtensionDocumentMessageContext context,
      CancellationToken cancellationToken)
    => context.TextDocument is Document document
        ? CreateTypeFromUsageService.CreateTypeFromUsageAsync(
            document,
            message.Line,
            message.Character,
            cancellationToken)
        : Task.FromResult(CreateTypeFromUsageResponse.No("Document is not a source document."));
}