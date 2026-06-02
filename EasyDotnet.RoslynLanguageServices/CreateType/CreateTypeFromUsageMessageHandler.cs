using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Extensions;

namespace EasyDotnet.RoslynLanguageServices.CreateType;

public sealed class CreateTypeFromUsageMessageHandler
    : IExtensionDocumentMessageHandler<CreateTypeFromUsageRequest, CreateTypeFromUsageResponse>
{
  public Task<CreateTypeFromUsageResponse> ExecuteAsync(
      CreateTypeFromUsageRequest message,
      ExtensionMessageContext context,
      Document document,
      CancellationToken cancellationToken)
    => CreateTypeFromUsageService.CreateTypeFromUsageAsync(
        document,
        message.Line,
        message.Character,
        cancellationToken);
}
