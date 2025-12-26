namespace EasyDotnet.Application.Interfaces;

public interface IEditorProcessManagerService
{
  void CompleteJob(Guid jobId, int exitCode);
  void SetFailedToStart(Guid jobId, string message);
  Guid RegisterJob();
  Task<int> WaitForExitAsync(Guid jobId);
}
