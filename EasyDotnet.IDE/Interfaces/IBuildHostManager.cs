using EasyDotnet.BuildServer.Contracts;

namespace EasyDotnet.IDE.Interfaces;

public interface IBuildHostManager
{
  void Dispose();
  ValueTask DisposeAsync();
  IAsyncEnumerable<ProjectEvaluationResult> GetProjectPropertiesBatchAsync(GetProjectPropertiesBatchRequest request, CancellationToken cancellationToken);
  Task<GetWatchListResponse> GetProjectWatchListAsync(GetWatchListRequest request, CancellationToken cancellationToken);
  IAsyncEnumerable<RestoreResult> RestoreNugetPackagesAsync(RestoreRequest request, CancellationToken cancellationToken);
  IAsyncEnumerable<BatchBuildResult> BatchBuildAsync(BatchBuildRequest request, CancellationToken cancellationToken);
  Task<ConvertSingleFileResponse> ConvertFileToProjectAsync(string entryPointFilePath, CancellationToken cancellationToken);
  Task<BuildServerDiagnosticsResponse> GetBuildServerDiagnosticsAsync(CancellationToken cancellationToken);
  Task<InstalledPackageReference[]> ListPackageReferencesAsync(string projectPath, CancellationToken cancellationToken);
}