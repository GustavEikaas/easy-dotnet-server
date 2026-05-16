using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IProjXWorkspaceContext
{
  Uri? RootUri { get; }
  void Initialize(InitializeParams initializeParams);
}

public sealed class ProjXWorkspaceContext : IProjXWorkspaceContext
{
  private readonly object _lock = new();
  private Uri? _rootUri;

  public Uri? RootUri
  {
    get
    {
      lock (_lock) return _rootUri;
    }
  }

  public void Initialize(InitializeParams initializeParams)
  {
    lock (_lock)
    {
      _rootUri = initializeParams.RootUri;
#pragma warning disable CS0618 // RootPath is needed as a fallback for older clients.
      var rootPath = initializeParams.RootPath;
#pragma warning restore CS0618
      if (_rootUri is null && !string.IsNullOrWhiteSpace(rootPath))
      {
        _rootUri = new Uri(Path.GetFullPath(rootPath));
      }
    }
  }
}