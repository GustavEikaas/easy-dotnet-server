namespace EasyDotnet.Application.Interfaces;

public interface IAppWrapperManager
{
  Task<IAppWrapperHandle> GetOrSpawnAsync(CancellationToken ct);
}