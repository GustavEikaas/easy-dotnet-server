using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.TestRunner.Lock;
using EasyDotnet.IDE.TestRunner.Models;

namespace EasyDotnet.IDE.TestRunner.Adapters;

/// <summary>
/// Seam between framework-agnostic orchestration and framework-specific protocol.
/// Implementations: VsTestAdapter, MtpAdapter.
///
/// All methods use callbacks rather than return values so the executor can
/// stream registerTest / updateStatus notifications in real time.
/// </summary>
public interface ITestAdapter
{
  /// <summary>
  /// Enumerate all tests in a compiled DLL.
  /// onDiscovered is called for each test as it arrives — do not buffer.
  /// </summary>
  Task DiscoverAsync(
      ValidatedDotnetProject project,
      Func<DiscoveredTest, Task> onDiscovered,
      OperationControl control,
      CancellationToken ct);

  /// <summary>
  /// Run specific tests by their native IDs.
  /// onResult is called as each test completes.
  /// </summary>
  Task RunAsync(
      ValidatedDotnetProject project,
      IReadOnlyList<string> nativeIds,
      Func<TestRunResult, Task> onResult,
      OperationControl control,
      CancellationToken ct);

  /// <summary>
  /// Debug specific tests by their native IDs.
  /// onResult is called as each test completes.
  /// </summary>
  Task DebugAsync(
      ValidatedDotnetProject project,
      IReadOnlyList<string> nativeIds,
      Func<TestRunResult, Task> onResult,
      OperationControl control,
      CancellationToken ct);
}