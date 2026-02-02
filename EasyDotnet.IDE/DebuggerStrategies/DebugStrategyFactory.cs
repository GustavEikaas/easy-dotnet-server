using EasyDotnet.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.DebuggerStrategies;

public interface IDebugStrategyFactory
{
  PidVsTestStrategy CreatePidVsTestStrategy(int pid);
  VsTestStrategy CreateVsTestStrategy();
  StandardLaunchStrategy CreateStandardLaunchStrategy(string? launchProfileName);
}

public class DebugStrategyFactory(ILoggerFactory loggerFactory, ILaunchProfileService launchProfileService) : IDebugStrategyFactory
{
  public PidVsTestStrategy CreatePidVsTestStrategy(int pid) => new(
    loggerFactory.CreateLogger<PidVsTestStrategy>(),
    pid);

  public VsTestStrategy CreateVsTestStrategy() => new(
    loggerFactory.CreateLogger<VsTestStrategy>());

  public StandardLaunchStrategy CreateStandardLaunchStrategy(string? launchProfileName) => new(
    launchProfileName,
    launchProfileService);
}