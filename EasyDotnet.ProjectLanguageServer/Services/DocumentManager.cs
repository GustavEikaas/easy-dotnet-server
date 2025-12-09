using System.Collections.Concurrent;

namespace EasyDotnet.ProjectLanguageServer.Services;

public interface IDocumentManager
{
  void OpenDocument(Uri uri, string text, int version);
  void UpdateDocument(Uri uri, string text, int version);
  void CloseDocument(Uri uri);
  string? GetDocumentContent(Uri uri);
  int GetDocumentVersion(Uri uri);
}

public class DocumentManager : IDocumentManager
{
  private readonly ConcurrentDictionary<Uri, DocumentState> _documents = new();

  public void OpenDocument(Uri uri, string text, int version) => _documents[uri] = new DocumentState(text, version);

  public void UpdateDocument(Uri uri, string text, int version) => _documents[uri] = new DocumentState(text, version);

  public void CloseDocument(Uri uri) => _documents.TryRemove(uri, out _);

  public string? GetDocumentContent(Uri uri) => _documents.TryGetValue(uri, out var state) ? state.Text : null;

  public int GetDocumentVersion(Uri uri) => _documents.TryGetValue(uri, out var state) ? state.Version : -1;

  private record DocumentState(string Text, int Version);
}