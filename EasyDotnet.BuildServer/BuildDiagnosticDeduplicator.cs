using EasyDotnet.BuildServer.Contracts;

namespace EasyDotnet.BuildServer.Handlers;

internal static class BuildDiagnosticDeduplicator
{
  internal static BuildDiagnostic[] Deduplicate(IEnumerable<BuildDiagnostic> diagnostics) =>
      [.. diagnostics.Distinct()];
}