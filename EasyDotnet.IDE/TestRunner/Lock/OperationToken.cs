namespace EasyDotnet.IDE.TestRunner.Lock;

public sealed class OperationToken : IDisposable
{
  public string OperationName { get; }

  private readonly CancellationTokenSource _linkedCts;
  private readonly Action _onDispose;
  private bool _disposed;

  public CancellationToken Ct => _linkedCts.Token;

  internal OperationToken(
      string operationName,
      CancellationToken rpcCancellationToken,
      Action onDispose)
  {
    OperationName = operationName;
    _onDispose = onDispose;
    _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(rpcCancellationToken);
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _linkedCts.Dispose();
    _onDispose();
  }
}