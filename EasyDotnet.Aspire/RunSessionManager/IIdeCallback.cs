using EasyDotnet.Aspire.Contracts;

namespace EasyDotnet.Aspire.RunSessionManager;

public interface IIdeCallback
{
  Task<int> RunManagedResourceAsync(RunManagedResourceRequest request, CancellationToken ct);

  Task StopManagedResourceAsync(string runId, CancellationToken ct);
}

public interface INotificationSink
{
  Task SendAsync(object notification, CancellationToken ct);
}