namespace EasyDotnet.Application.Interfaces;

public interface INotificationService
{
  Task NotifyProjectChanged(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug");
  Task NotifyUpdateAvailable(Version currentVersion, Version availableVersion, string updateType);
  Task RequestRestoreAsync(string targetPath);
}