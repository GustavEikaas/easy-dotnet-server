using EasyDotnet.IDE.Models.Client;

namespace EasyDotnet.IDE.Interfaces;

public interface IAppWrapperHandle
{
  Task SendRunCommandAsync(Guid jobId, RunCommand command, CancellationToken ct);
  Task TerminateAsync();
}
