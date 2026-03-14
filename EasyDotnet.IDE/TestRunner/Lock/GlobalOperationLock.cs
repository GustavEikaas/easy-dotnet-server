namespace EasyDotnet.IDE.TestRunner.Lock;

public class GlobalOperationLock
{
  private readonly SemaphoreSlim _semaphore = new(1, 1);
  private readonly object _sync = new();

  // Monotonic operation id counter.
  // Used to:
  // 1) tag every acquired token with a unique id for stale-update gating, and
  // 2) invalidate any in-flight token on ForceReleaseIfHeld() so it can't double-release.
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

  /// <summary>
  /// Best-effort recovery: makes the lock acquirable again even if the current operation is hung.
  /// Invalidates the current operationId so stale tokens cannot double-release.
  /// </summary>
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

    return new OperationToken(opId, operationName, rpcCancellationToken, operationCancellationToken, Release);
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