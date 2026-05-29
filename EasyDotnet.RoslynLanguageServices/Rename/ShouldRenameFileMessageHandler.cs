using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Extensions;

namespace EasyDotnet.RoslynLanguageServices.Rename;

public sealed class ShouldRenameFileMessageHandler
    : IExtensionDocumentMessageHandler<ShouldRenameFileRequest, ShouldRenameFileResponse>
{
  public Task<ShouldRenameFileResponse> ExecuteAsync(
      ShouldRenameFileRequest message,
      ExtensionMessageContext context,
      Document document,
      CancellationToken cancellationToken)
    => RenameFileDecisionService.ShouldRenameFileAsync(
        document,
        message.Line,
        message.Character,
        message.NewName,
        cancellationToken);
}
