using Microsoft.CodeAnalysis.Extensions;

namespace EasyDotnet.RoslynLanguageServices.Diagnostics;

public sealed class WorkspaceDiagnosticsMessageHandler
    : IExtensionWorkspaceMessageHandler<WorkspaceDiagnosticsRequest, WorkspaceDiagnosticsResponse>
{
  public Task<WorkspaceDiagnosticsResponse> ExecuteAsync(
      WorkspaceDiagnosticsRequest message,
      ExtensionWorkspaceMessageContext context,
      CancellationToken cancellationToken)
    => WorkspaceDiagnosticsService.GetDiagnosticsAsync(
        context.Solution,
        message.TargetPath,
        message.IncludeWarnings,
        cancellationToken);
}