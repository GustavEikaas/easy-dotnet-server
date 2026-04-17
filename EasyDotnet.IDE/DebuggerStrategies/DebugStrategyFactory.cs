using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.DebuggerStrategies;

public interface IDebugStrategyFactory
{
  RunInTerminalStrategy CreateRunInTerminalStrategy(ValidatedDotnetProject project, string? launchProfileName, string? cliArgs = null);
  StandardAttachStrategy CreateStandardAttachStrategy(int pid, string? cwd = null);
  VsTestStrategy CreateVsTestStrategy(string targetPath, string? projectDir);
}

public class DebugStrategyFactory(
  ILoggerFactory loggerFactory,
  IHttpClientFactory httpClientFactory,
  ILaunchProfileService launchProfileService,
  IStartupHookService startupHookService,
  IAppWrapperManager appWrapperManager) : IDebugStrategyFactory
{
  public RunInTerminalStrategy CreateRunInTerminalStrategy(ValidatedDotnetProject project, string? launchProfileName, string? cliArgs = null) => new(
    project,
    launchProfileName,
    cliArgs,
    loggerFactory.CreateLogger<RunInTerminalStrategy>(),
    startupHookService,
    httpClientFactory,
    launchProfileService,
    appWrapperManager);

  public StandardAttachStrategy CreateStandardAttachStrategy(int pid, string? cwd = null) => new(
    loggerFactory.CreateLogger<StandardAttachStrategy>(),
    pid,
    cwd);

  public VsTestStrategy CreateVsTestStrategy(string targetPath, string? projectDir) => new(
    targetPath,
    projectDir,
    loggerFactory.CreateLogger<VsTestStrategy>());
}