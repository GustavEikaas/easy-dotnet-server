using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.ProjXLanguageServer.Tests.Helpers;

public static class CompletionTestFactory
{
  public static CompletionService Create(
      FakeNugetSearchService? nuget = null,
      FakeLspProgressReporter? progress = null) =>
      new(
          nuget ?? new FakeNugetSearchService(),
          progress ?? new FakeLspProgressReporter(),
          NullLogger<CompletionService>.Instance);
}