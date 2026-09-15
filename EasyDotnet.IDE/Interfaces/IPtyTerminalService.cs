using EasyDotnet.IDE.Models.Client;

namespace EasyDotnet.IDE.Interfaces;

public interface IPtyTerminalService
{
  Task<int> StartAsync(Guid jobId, RunCommand command, int rows, int cols, CancellationToken ct = default);

  void Write(Guid jobId, byte[] data);

  void Resize(Guid jobId, int cols, int rows);

  void Kill(Guid jobId);
}