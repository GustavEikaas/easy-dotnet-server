namespace EasyDotnet.IDE.TestRunner.Lock;

public class GlobalOperationLock
{
  private readonly SemaphoreSlim _semaphore = new(1, 1);
  private readonly object _sync = new();
  private long _operationIdCounter;
  private long _currentOperationId;

  public long CurrentOperationId => Volatile.Read(ref _currentOperationId);

  public OperationToken? TryAcquire(
      string operationName,
      CancellationToken rpcCancellationToken,
      CancellationToken operationCancellationToken = default)
  {
    if (!_semaphore.Wait(0, rpcCancellationToken)) { return null; }
    return CreateToken(operationName, rpcCancellationToken, operationCancellationToken);
  }

  public async Task<OperationToken> WaitAcquireAsync(
      string operationName,
      CancellationToken rpcCancellationToken,
      CancellationToken operationCancellationToken = default)
  {
    await _semaphore.WaitAsync(rpcCancellationToken);
    return CreateToken(operationName, rpcCancellationToken, operationCancellationToken);
  }

  public bool IsLocked => _semaphore.CurrentCount == 0;
  public string? CurrentOperationName { get; private set; }

  public void ForceReleaseIfHeld()
  {
    lock (_sync)
    {
      _currentOperationId = Interlocked.Increment(ref _operationIdCounter);
      CurrentOperationName = null;

      if (_semaphore.CurrentCount == 0)
      {
        _semaphore.Release();
      }
    }
  }

  private OperationToken CreateToken(
      string operationName,
      CancellationToken rpcCancellationToken,
      CancellationToken operationCancellationToken)
  {
    var opId = Interlocked.Increment(ref _operationIdCounter);

    lock (_sync)
    {
      _currentOperationId = opId;
      CurrentOperationName = operationName;
    }

    return new OperationToken(opId, rpcCancellationToken, operationCancellationToken, Release);
  }

  private void Release(long operationId)
  {
    lock (_sync)
    {
      if (operationId != _currentOperationId)
        return;

      _currentOperationId = 0;
      CurrentOperationName = null;
      _semaphore.Release();
    }
  }
}