namespace EasyDotnet.IDE.Interfaces;

public interface IOpenBufferService
{
  void OnBufferOpened(string path);
  void OnBufferClosed(string path);
  bool IsOpen(string path);
  IReadOnlySet<string> Snapshot();

  event Action<string>? BufferOpened;
  event Action<string>? BufferClosed;
}
