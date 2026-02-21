using EasyDotnet.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.DebuggerStrategies;

public interface IDebugStrategyFactory
{
  ExternalConsoleStrategy CreateExternalConsoleStrategy();
  StandardLaunchStrategy CreateStandardLaunchStrategy(string? launchProfileName);
  StandardAttachStrategy CreateStandardAttachStrategy(int pid);
  VsTestStrategy CreateVsTestStrategy();
}

public class DebugStrategyFactory(ILoggerFactory loggerFactory, ILaunchProfileService launchProfileService) : IDebugStrategyFactory
{
  //TODO: support launch profiles
  public ExternalConsoleStrategy CreateExternalConsoleStrategy() => new(
    loggerFactory.CreateLogger<ExternalConsoleStrategy>());

  public StandardLaunchStrategy CreateStandardLaunchStrategy(string? launchProfileName) => new(
    launchProfileName,
    launchProfileService);

  public StandardAttachStrategy CreateStandardAttachStrategy(int pid) => new(
    loggerFactory.CreateLogger<StandardAttachStrategy>(),
    pid);

  public VsTestStrategy CreateVsTestStrategy() => new(
    loggerFactory.CreateLogger<VsTestStrategy>());
}