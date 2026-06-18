using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Extensions;

namespace EasyDotnet.RoslynLanguageServices.ImportMissingNamespaces;

public sealed class ImportMissingNamespacesMessageHandler
    : IExtensionDocumentMessageHandler<ImportMissingNamespacesRequest, ImportMissingNamespacesResponse>
{
  public Task<ImportMissingNamespacesResponse> ExecuteAsync(
      ImportMissingNamespacesRequest message,
      ExtensionMessageContext context,
      Document document,
      CancellationToken cancellationToken)
    => ImportMissingNamespacesService.ImportMissingNamespacesAsync(document, cancellationToken);
}