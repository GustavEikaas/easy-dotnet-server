using EasyDotnet.IDE.Services;

namespace EasyDotnet.IDE.Interfaces;

public interface INotificationService
{
  Task NotifyProjectChanged(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug");
  Task NotifySolutionProjectsLoaded();
  Task NotifyUpdateAvailable(Version currentVersion, Version availableVersion, string updateType);
  Task NotifyRoslynUpdateAvailable(string? currentVersion, string availableVersion, string minimumRecommendedVersion, bool isBelowRecommended);
  Task NotifyActiveProjectChanged(string? projectPath, string? projectName, string? launchProfile);
  Task NotifyRunningProcessesChangedAsync(RunningSessionInfo[] projects);
}
