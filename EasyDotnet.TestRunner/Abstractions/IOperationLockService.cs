namespace EasyDotnet.TestRunner.Abstractions;

public interface IOperationLockService
{
  Task<IDisposable> LockGlobalAsync(CancellationToken clientToken);
  Task<IDisposable> LockNodeAsync(string nodeId, CancellationToken clientToken);
}