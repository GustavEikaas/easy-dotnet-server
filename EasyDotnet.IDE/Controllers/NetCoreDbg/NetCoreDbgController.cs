using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Types;
using EasyDotnet.MsBuild;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.NetCoreDbg;

public sealed record DebuggerStartRequest(
  string TargetPath,
  string? TargetFramework,
  string? Configuration,
  string? LaunchProfileName
);

public sealed record DebuggerStartResponse(bool Success, int Port);

public class NetCoreDbgController(
  IDebugOrchestrator debugOrchestrator,
  IDebugStrategyFactory debugStrategyFactory,
  IMsBuildService msBuildService) : BaseController
{
  [JsonRpcMethod("debugger/start")]
  public async Task<DebuggerStartResponse> StartDebugger(DebuggerStartRequest request, CancellationToken cancellationToken)
  {
    var project = await msBuildService.GetOrSetProjectPropertiesAsync(
                request.TargetPath,
                request.TargetFramework,
                request.Configuration ?? "Debug",
                cancellationToken);

    var strategy = ResolveStrategy(project, request.LaunchProfileName);

    var session = await debugOrchestrator.StartClientDebugSessionAsync(
        request.TargetPath,
        request,
        strategy,
        cancellationToken);

    return new DebuggerStartResponse(true, session.Port);
  }

  private IDebugSessionStrategy ResolveStrategy(DotnetProject project, string? launchProfileName) =>
    project.IsVsTest()
      ? debugStrategyFactory.CreateVsTestStrategy()
      : debugStrategyFactory.CreateExternalConsoleStrategy();
}