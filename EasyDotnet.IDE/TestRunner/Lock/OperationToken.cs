namespace EasyDotnet.IDE.TestRunner.Lock;

public sealed class OperationToken : IDisposable
{
  public long OperationId { get; }

  private readonly CancellationTokenSource _linkedCts;
  private readonly Action<long> _onDispose;
  private bool _disposed;

  public CancellationToken Ct => _linkedCts.Token;

  internal OperationToken(
      long operationId,
      CancellationToken rpcCancellationToken,
      CancellationToken operationCancellationToken,
      Action<long> onDispose)
  {
    OperationId = operationId;
    _onDispose = onDispose;
    _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(rpcCancellationToken, operationCancellationToken);
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _linkedCts.Dispose();
    _onDispose(OperationId);
  }
}