using EasyDotnet.Domain.Models.Test;

namespace EasyDotnet.Application.Interfaces;

public interface IVsTestService
{
  IAsyncEnumerable<DiscoveredTest> DiscoverAsync(string[] dllPaths, CancellationToken ct);

  IAsyncEnumerable<TestRunResult> RunTestsAsync(string dllPath, IEnumerable<Guid>? testIds, CancellationToken ct);
}