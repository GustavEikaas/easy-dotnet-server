namespace EasyDotnet.IDE.Interfaces;

public interface IAppWrapperManager
{
  Task<IAppWrapperHandle> GetOrSpawnAsync(CancellationToken ct);
}