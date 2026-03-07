namespace EasyDotnet.IDE.TestRunner.Lock;

/// <summary>
/// Enforces the invariant that only one user-initiated transaction runs at a time.
///
/// Cancellation flow:
///   1. StreamJsonRpc receives $/cancelRequest from the client.
///   2. StreamJsonRpc cancels the CancellationToken it injected into the handler.
///   3. The handler passed that token to TryAcquire, which linked it into
///      OperationToken.Cts via CreateLinkedTokenSource.
///   4. Everything downstream uses token.Ct — cancellation propagates naturally.
///
/// No manual $/cancelRequest handler is required.
/// </summary>
public class GlobalOperationLock
{
  private volatile OperationToken? _current;
  private readonly object _gate = new();

  /// <summary>
  /// Attempts to acquire the lock for a named operation.
  /// The provided rpcCancellationToken (from StreamJsonRpc) is linked into the
  /// returned token so that $/cancelRequest automatically cancels the pipeline.
  /// Returns null if an operation is already in flight.
  /// </summary>
  public OperationToken? TryAcquire(string operationName, CancellationToken rpcCancellationToken)
  {
    lock (_gate)
    {
      if (_current is not null) return null;

      var token = new OperationToken(operationName, rpcCancellationToken, () =>
      {
        lock (_gate) { _current = null; }
      });

      _current = token;
      return token;
    }
  }

  public bool IsLocked => _current is not null;
  public string? CurrentOperationName => _current?.OperationName;
}