using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.ProjXLanguageServer.Tests.Helpers;

public static class CompletionTestFactory
{
  public static CompletionService Create(FakeNugetSearchService? nuget = null) =>
      new(nuget ?? new FakeNugetSearchService(), NullLogger<CompletionService>.Instance);
}