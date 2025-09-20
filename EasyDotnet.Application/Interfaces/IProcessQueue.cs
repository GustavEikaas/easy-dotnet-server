namespace EasyDotnet.Application.Interfaces;


public interface IProcessQueue
{
  /// <summary>
  /// Gets the number of available slots in the process queue.
  /// </summary>
  /// <returns>
  /// The number of available slots (<see cref="int"/>) for process execution.
  /// </returns>
  int CurrentCount();
  /// <summary>
  /// Executes a process asynchronously with concurrency control.
  /// </summary>
  /// <param name="command">
  /// The name of the process or executable file to start.
  /// </param>
  /// <param name="arguments">
  /// The command-line arguments to pass to the process.
  /// </param>
  /// <param name="options">
  /// Optional <see cref="ProcessOptions"/> to configure timeout and kill behavior.  
  /// If <c>null</c>, defaults are applied.
  /// </param>
  /// <param name="cancellationToken">
  /// A <see cref="CancellationToken"/> to observe while waiting for the process to complete.
  /// </param>
  /// <returns>
  /// A task that returns a tuple:  
  /// <list type="bullet">
  ///   <item><c>Success</c>: <c>true</c> if the process exits with code 0; otherwise, <c>false</c>.</item>
  ///   <item><c>StdOut</c>: The captured standard output from the process.</item>
  ///   <item><c>StdErr</c>: The captured standard error output from the process.</item>
  /// </list>
  /// </returns>
  Task<(bool Success, string StdOut, string StdErr)> RunProcessAsync(string command, string arguments, ProcessOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Options used to configure the behavior of a process execution.
/// </summary>
/// <param name="KillOnTimeout">
/// If <c>true</c>, the process will be forcibly killed if it exceeds the timeout period.  
/// If <c>false</c>, the process will remain running even after timeout.
/// </param>
/// <param name="CancellationTimeout">
/// Optional timeout for process execution.  
/// If not specified, a default timeout of 2 minutes is applied.
/// </param>
public record ProcessOptions(
    bool KillOnTimeout = false,
    TimeSpan? CancellationTimeout = null
);