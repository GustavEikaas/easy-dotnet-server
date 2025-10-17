namespace EasyDotnet.Application.Interfaces;

public interface INotificationService
{
  Task NotifyProjectChanged(string projectPath, string? targetFrameworkMoniker = null, string configuration = "Debug");
  Task RequestRestoreAsync(string targetPath);
  Task DisplayError(string message);
  Task DisplayWarning(string message);
  Task DisplayMessage(string message);
}