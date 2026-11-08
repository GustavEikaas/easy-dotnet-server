using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Extensions;

namespace EasyDotnet.RoslynLanguageServices.Rename;

public sealed class ShouldRenameFileMessageHandler
    : IExtensionDocumentMessageHandler<ShouldRenameFileRequest, ShouldRenameFileResponse>
{
  public Task<ShouldRenameFileResponse> ExecuteAsync(
      ShouldRenameFileRequest message,
      ExtensionDocumentMessageContext context,
      CancellationToken cancellationToken)
    => context.TextDocument is Document document
        ? RenameFileDecisionService.ShouldRenameFileAsync(
            document,
            message.Line,
            message.Character,
            message.NewName,
            cancellationToken)
        : Task.FromResult(ShouldRenameFileResponse.No("Document is not a source document."));
}