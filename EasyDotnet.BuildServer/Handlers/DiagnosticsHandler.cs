using EasyDotnet.BuildServer.Contracts;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class DiagnosticsHandler(MsBuildInstance msBuildInstance)
{
  [JsonRpcMethod("diagnostics/buildserver")]
  public BuildServerDiagnosticsResponse GetDiagnostics() =>
      new(
          RuntimeVersion: Environment.Version.ToString(),
          RuntimeVersionMajor: Environment.Version.Major,
          MsBuildVersion: msBuildInstance.Version.ToString(),
          MsBuildPath: msBuildInstance.MSBuildPath);
}