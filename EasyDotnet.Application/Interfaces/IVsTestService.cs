using EasyDotnet.Domain.Models.Test;

namespace EasyDotnet.Application.Interfaces;

public interface IVsTestService
{
  IAsyncEnumerable<DiscoveredTest> DiscoverAsync(string[] dllPaths, CancellationToken cancellationToken);
  List<TestRunResult> RunTests(string dllPath, Guid[] testIds);
}