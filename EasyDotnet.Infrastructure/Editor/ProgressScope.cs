using EasyDotnet.Application.Interfaces;

namespace EasyDotnet.Infrastructure.Editor;

public sealed class ProgressScope : IDisposable
{
  private readonly IEditorService _client;
  private readonly string _token;

  public ProgressScope(IEditorService client, string title, string message)
  {
    _client = client;
    _token = Guid.NewGuid().ToString();
    _client.SendProgressStart(_token, title, message);
  }
  public void Report(string message, int? percentage = null) => _ = _client.SendProgressUpdate(_token, message: message, percentage: percentage);
  public void Dispose() => _client.SendProgressEnd(_token);
}

public interface IProgressScopeFactory
{
  ProgressScope Create(string title, string message);
}

public sealed class ProgressScopeFactory(IEditorService editorService) : IProgressScopeFactory
{
  public ProgressScope Create(string title, string message) => new(editorService, title, message);
}