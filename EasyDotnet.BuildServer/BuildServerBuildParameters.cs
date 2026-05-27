using Microsoft.Build.Execution;

namespace EasyDotnet.BuildServer;

internal static class BuildServerBuildParameters
{
  public static BuildParameters Create(IEnumerable<Microsoft.Build.Framework.ILogger> loggers)
  {
    return new BuildParameters
    {
      // Keep build/restore task assemblies out of the long-lived BuildServer process.
      // Workload tasks can depend on assemblies with versions that differ from the
      // server's own dependency context.
      DisableInProcNode = true,
      EnableNodeReuse = false,
      MaxNodeCount = Math.Max(1, Environment.ProcessorCount),
      Loggers = loggers,
    };
  }
}