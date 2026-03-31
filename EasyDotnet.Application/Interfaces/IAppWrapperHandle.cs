using EasyDotnet.Domain.Models.Client;

namespace EasyDotnet.Application.Interfaces;

public interface IAppWrapperHandle
{
    Task SendRunCommandAsync(Guid jobId, RunCommand command, CancellationToken ct);
    Task TerminateAsync();
}
