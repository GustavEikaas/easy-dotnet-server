using System.Collections.Concurrent;
using EasyDotnet.TestRunner.Abstractions;

namespace EasyDotnet.TestRunner.Services;

public class OperationLockService : IOperationLockService
{
  private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeOperations = new();
  private readonly SemaphoreSlim _globalDiscoveryLock = new(1, 1);

  /// <summary>
  /// Attempts to lock a specific node for an operation (Run/Debug).
  /// </summary>
  public async Task<IDisposable> LockNodeAsync(string nodeId, CancellationToken clientToken)
  {
    // RFC 8.1: Check if already running
    if (_activeOperations.ContainsKey(nodeId))
    {
      throw new InvalidOperationException($"Operation already in progress for node {nodeId}");
    }

    // Check if a global discovery is happening (Blocks everything)
    if (_globalDiscoveryLock.CurrentCount == 0)
    {
      throw new InvalidOperationException("Cannot run tests while discovery is in progress.");
    }

    var cts = CancellationTokenSource.CreateLinkedTokenSource(clientToken);
    if (!_activeOperations.TryAdd(nodeId, cts))
    {
      throw new InvalidOperationException($"Concurrency collision for node {nodeId}");
    }

    return new OperationScope(nodeId, _activeOperations, cts);
  }

  /// <summary>
  /// Locks the entire runner for Discovery.
  /// </summary>
  public async Task<IDisposable> LockGlobalAsync(CancellationToken clientToken)
  {
    // Wait for any existing discovery to finish
    if (!await _globalDiscoveryLock.WaitAsync(0))
    {
      throw new InvalidOperationException("Discovery already in progress.");
    }

    // Optional: You might want to check if ANY tests are running and fail if so
    if (!_activeOperations.IsEmpty)
    {
      _globalDiscoveryLock.Release();
      throw new InvalidOperationException("Cannot start discovery while tests are running.");
    }

    return new GlobalScope(_globalDiscoveryLock);
  }

  // Helper classes to release locks on Dispose using the 'using' keyword
  private class OperationScope(string id, ConcurrentDictionary<string, CancellationTokenSource> dict, CancellationTokenSource cts) : IDisposable
  {
    public void Dispose()
    {
      dict.TryRemove(id, out _);
      cts.Dispose();
    }
  }

  private class GlobalScope(SemaphoreSlim semaphore) : IDisposable
  {
    public void Dispose() => semaphore.Release();
  }
}