using System.Collections.Concurrent;

namespace EasyDotnet.Infrastructure.Dap;

public enum RequestOrigin
{
  Client,
  Proxy
}

public record RequestContext(
  RequestOrigin Origin,
  int OriginalSeq,
  TaskCompletionSource<Response> CompletionSource,
  CancellationToken CancellationToken
);

public interface IRequestTracker
{
  int RegisterClientRequest(int clientSeq);
  int RegisterProxyRequest(TaskCompletionSource<Response> tcs, CancellationToken cancellationToken);
  RequestContext? GetAndRemoveContext(int proxySeq);
  void Clear();
}

public class RequestTracker : IRequestTracker
{
  private readonly ConcurrentDictionary<int, RequestContext> _pendingRequests = new();
  private int _proxySeq;

  private int GetNextProxySeq() => Interlocked.Increment(ref _proxySeq);

  public int RegisterClientRequest(int clientSeq)
  {
    var proxySeq = GetNextProxySeq();
    var context = new RequestContext(
      RequestOrigin.Client,
      clientSeq,
      new TaskCompletionSource<Response>(),
      CancellationToken.None
    );

    if (!_pendingRequests.TryAdd(proxySeq, context))
    {
      throw new InvalidOperationException($"Failed to register client request with proxy seq {proxySeq}");
    }

    return proxySeq;
  }

  public int RegisterProxyRequest(TaskCompletionSource<Response> tcs, CancellationToken cancellationToken)
  {
    var proxySeq = GetNextProxySeq();
    var context = new RequestContext(
      RequestOrigin.Proxy,
      proxySeq,
      tcs,
      cancellationToken
    );

    if (!_pendingRequests.TryAdd(proxySeq, context))
    {
      throw new InvalidOperationException($"Failed to register proxy request with seq {proxySeq}");
    }

    return proxySeq;
  }

  public RequestContext? GetAndRemoveContext(int proxySeq)
  {
    _pendingRequests.TryRemove(proxySeq, out var context);
    return context;
  }

  public void Clear()
  {
    foreach (var kvp in _pendingRequests)
    {
      if (kvp.Value.Origin == RequestOrigin.Proxy)
      {
        kvp.Value.CompletionSource.TrySetCanceled();
      }
    }
    _pendingRequests.Clear();
  }
}