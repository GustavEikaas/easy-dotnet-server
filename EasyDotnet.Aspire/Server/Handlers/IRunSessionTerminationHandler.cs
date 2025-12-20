using EasyDotnet.Aspire.Session;

namespace EasyDotnet.Aspire.Server.Handlers;

public interface IRunSessionTerminationHandler
{
  Task OnSessionTerminatedAsync(
      RunSession session,
      int? exitCode,
      CancellationToken cancellationToken = default);
}