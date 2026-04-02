using EasyDotnet.IDE.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.DebuggerStrategies;

public interface IDebugStrategyFactory
{
  RunInTerminalStrategy CreateRunInTerminalStrategy(string? launchProfileName, string? cliArgs = null);
  StandardLaunchStrategy CreateStandardLaunchStrategy();
  StandardAttachStrategy CreateStandardAttachStrategy(int pid);
  VsTestStrategy CreateVsTestStrategy();
}

public class DebugStrategyFactory(
  ILoggerFactory loggerFactory,
  IHttpClientFactory httpClientFactory,
  ILaunchProfileService launchProfileService,
  IStartupHookService startupHookService,
  IAppWrapperManager appWrapperManager) : IDebugStrategyFactory
{
  public RunInTerminalStrategy CreateRunInTerminalStrategy(string? launchProfileName, string? cliArgs = null) => new(
    launchProfileName,
    cliArgs,
    loggerFactory.CreateLogger<RunInTerminalStrategy>(),
    startupHookService,
    httpClientFactory,
    launchProfileService,
    appWrapperManager);

  public StandardLaunchStrategy CreateStandardLaunchStrategy() => new();

  public StandardAttachStrategy CreateStandardAttachStrategy(int pid) => new(
    loggerFactory.CreateLogger<StandardAttachStrategy>(),
    pid);

  public VsTestStrategy CreateVsTestStrategy() => new(
    loggerFactory.CreateLogger<VsTestStrategy>());
}