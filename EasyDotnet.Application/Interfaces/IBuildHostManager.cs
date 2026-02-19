using EasyDotnet.BuildServer.Contracts;

namespace EasyDotnet.Application.Interfaces;

public interface IBuildHostManager
{
  void Dispose();
  ValueTask DisposeAsync();
  Task<IAsyncEnumerable<ProjectEvaluationResult>> GetProjectPropertiesBatchAsync(GetProjectPropertiesBatchRequest request, CancellationToken cancellationToken);
  Task<GetWatchListResponse> GetProjectWatchListAsync(GetWatchListRequest request, CancellationToken cancellationToken);
  Task<IAsyncEnumerable<RestoreResult>> RestoreNugetPackagesAsync(RestoreRequest request, CancellationToken cancellationToken);
}