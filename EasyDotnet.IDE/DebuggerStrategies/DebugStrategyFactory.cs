using EasyDotnet.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.DebuggerStrategies;

public interface IDebugStrategyFactory
{
  StandardAttachStrategy CreateStandardAttachStrategy(int pid);
  VsTestStrategy CreateVsTestStrategy();
  StandardLaunchStrategy CreateStandardLaunchStrategy(string? launchProfileName);
}

public class DebugStrategyFactory(ILoggerFactory loggerFactory, ILaunchProfileService launchProfileService) : IDebugStrategyFactory
{
  public StandardAttachStrategy CreateStandardAttachStrategy(int pid) => new(
    loggerFactory.CreateLogger<StandardAttachStrategy>(),
    pid);

  public VsTestStrategy CreateVsTestStrategy() => new(
    loggerFactory.CreateLogger<VsTestStrategy>());

  public StandardLaunchStrategy CreateStandardLaunchStrategy(string? launchProfileName) => new(
    launchProfileName,
    launchProfileService);
}