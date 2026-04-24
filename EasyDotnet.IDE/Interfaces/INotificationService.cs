namespace EasyDotnet.IDE.Interfaces;

public interface INotificationService
{
  Task NotifyProjectChanged(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug");
  Task NotifyUpdateAvailable(Version currentVersion, Version availableVersion, string updateType);
  Task NotifyActiveProjectChanged(string? projectPath, string? projectName, string? launchProfile);
}