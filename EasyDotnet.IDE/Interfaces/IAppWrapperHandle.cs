using EasyDotnet.IDE.Models.Client;

namespace EasyDotnet.IDE.Interfaces;

public interface IAppWrapperHandle
{
  /// <summary>
  /// Invokes a command in the external terminal and responds with the pid
  /// </summary>
  /// <returns>PID of the started process</returns>
  Task<int> SendRunCommandAsync(Guid jobId, RunCommand command, CancellationToken ct);
  Task TerminateAsync();
}