namespace EasyDotnet.Application.Interfaces;

public enum TerminalSlot
{
  Managed,
  LongRunning
}

public interface IEditorProcessManagerService
{
  void CompleteJob(Guid jobId, int exitCode);
  bool IsSlotBusy(TerminalSlot slot);
  Guid RegisterJob(TerminalSlot slot);
  void SetFailedToStart(Guid jobId, TerminalSlot slot, string message);
  Task<int> WaitForExitAsync(Guid jobId, TerminalSlot slot);
}