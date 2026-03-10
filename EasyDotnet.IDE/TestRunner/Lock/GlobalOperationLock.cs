namespace EasyDotnet.IDE.TestRunner.Lock;

public class GlobalOperationLock
{
  private readonly SemaphoreSlim _semaphore = new(1, 1);

  public OperationToken? TryAcquire(string operationName, CancellationToken rpcCancellationToken)
  {
    if (!_semaphore.Wait(0, rpcCancellationToken)) { return null; }
    return CreateToken(operationName, rpcCancellationToken);
  }

  public async Task<OperationToken> WaitAcquireAsync(string operationName, CancellationToken ct)
  {
    await _semaphore.WaitAsync(ct);
    return CreateToken(operationName, ct);
  }

  public bool IsLocked => _semaphore.CurrentCount == 0;
  public string? CurrentOperationName { get; private set; }

  private OperationToken CreateToken(string operationName, CancellationToken ct)
  {
    CurrentOperationName = operationName;
    return new OperationToken(operationName, ct, () =>
    {
      CurrentOperationName = null;
      _semaphore.Release();
    });
  }
}