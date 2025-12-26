namespace EasyDotnet.Application.Interfaces;

public interface IEditorProcessManagerService
{
  void CompleteJob(Guid jobId, int exitCode);
  Guid RegisterJob();
  Task<int> WaitForExitAsync(Guid jobId);
}
