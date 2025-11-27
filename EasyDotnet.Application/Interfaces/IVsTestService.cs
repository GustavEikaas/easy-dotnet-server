using EasyDotnet.Domain.Models.Test;

namespace EasyDotnet.Application.Interfaces;

public interface IVsTestService
{
  List<DiscoveredTest> RunDiscover(string dllPath);
  List<TestRunResult> RunTests(string dllPath, Guid[] testIds);
}