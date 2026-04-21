namespace EasyDotnet.IDE.Interfaces;


public interface IProcessQueue
{
  int CurrentCount();
  Task<(bool Success, string StdOut, string StdErr)> RunProcessAsync(string command, string arguments, ProcessOptions? options = null, CancellationToken cancellationToken = default);
}

public record ProcessOptions(
    bool KillOnTimeout = false,
    TimeSpan? CancellationTimeout = null
);