using EasyDotnet.Controllers;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Logging;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.TestRunner.Service;
using EasyDotnet.IDE.Workspace.Services;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers;

public class ServerController(
    LogLevelState logLevelState,
    IBuildHostManager buildHostManager,
    IClientService clientService,
    ProjectEvaluationCache projectEvaluationCache,
    WorkspaceSessionRegistry workspaceSessionRegistry,
    TestRunnerService testRunnerService,
    DbContextCache dbContextCache,
    ILogger<ServerController> logger) : BaseController
{
  public sealed record SetLogLevelRequest(string Level);

  [JsonRpcMethod("_server/setLogLevel", UseSingleObjectParameterDeserialization = true)]
  public async Task SetLogLevel(SetLogLevelRequest request, CancellationToken ct)
  {
    var parsed = LogLevelState.Parse(request.Level);
    logLevelState.Set(parsed);
    logger.LogInformation("Log level set to {Level}", parsed);
    await buildHostManager.SetLogLevelAsync(parsed.ToString(), ct);
  }

  [JsonRpcMethod("_server/logdump")]
  public string[] LogDump() => [.. logLevelState.RingSink.Snapshot()];

  [JsonRpcMethod("_server/logdump/buildserver")]
  public Task<string[]> LogDumpBuildServer(CancellationToken ct) =>
      buildHostManager.GetLogsAsync(ct);

  [JsonRpcMethod("_server/test-reset")]
  public async Task TestReset(CancellationToken ct)
  {
    if (!string.Equals(
        Environment.GetEnvironmentVariable("EASYDOTNET_CONTAINER_TESTS"),
        "1",
        StringComparison.Ordinal))
    {
      throw new InvalidOperationException("_server/test-reset is only available in container tests.");
    }

    await buildHostManager.ResetAsync(ct);
    projectEvaluationCache.Clear();
    workspaceSessionRegistry.Clear();
    await testRunnerService.ResetForTestsAsync();
    dbContextCache.Clear();

    clientService.IsInitialized = false;
    clientService.UseVisualStudio = false;
    clientService.SupportsSingleFileExecution = false;
    clientService.ProjectInfo = null;
    clientService.ClientInfo = null;
    clientService.ClientOptions = null;

    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
    logger.LogDebug("Container test server state reset.");
  }
}