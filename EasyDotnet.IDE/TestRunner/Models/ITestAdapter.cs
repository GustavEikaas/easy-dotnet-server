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
        string dllPath,
        Func<DiscoveredTest, Task> onDiscovered,
        CancellationToken ct);

    /// <summary>
    /// Run specific tests by their native IDs.
    /// onResult is called as each test completes.
    /// </summary>
    Task RunAsync(
        string dllPath,
        IReadOnlyList<string> nativeIds,
        Func<TestRunResult, Task> onResult,
        CancellationToken ct);

    /// <summary>
    /// Debug specific tests by their native IDs.
    /// onResult is called as each test completes.
    /// </summary>
    Task DebugAsync(
        string dllPath,
        IReadOnlyList<string> nativeIds,
        Func<TestRunResult, Task> onResult,
        CancellationToken ct);
}
