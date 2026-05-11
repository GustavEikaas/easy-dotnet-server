namespace EasyDotnet.IDE.Interfaces;

public enum TerminalSlot
{
  Managed
}

public interface IEditorProcessManagerService
{
  void CompleteJob(Guid jobId, int exitCode);
  bool IsSlotBusy(TerminalSlot slot);
  Guid RegisterJob(TerminalSlot? slot = null);
  void SetFailedToStart(Guid jobId, TerminalSlot? slot, string message);
  Task<int> WaitForExitAsync(Guid jobId, TerminalSlot? slot = null);
}