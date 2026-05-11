using System.Collections.Concurrent;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IDocumentManager
{
  void OpenDocument(Uri uri, string text, int version);
  void UpdateDocument(Uri uri, string text, int version);
  void CloseDocument(Uri uri);
  string? GetDocumentContent(Uri uri);
  int GetDocumentVersion(Uri uri);
  CsprojDocument? GetDocument(Uri uri);
}

public class DocumentManager : IDocumentManager
{
  private readonly ConcurrentDictionary<Uri, CsprojDocument> _documents = new();

  public void OpenDocument(Uri uri, string text, int version) => _documents[uri] = new CsprojDocument(uri, text, version);

  public void UpdateDocument(Uri uri, string text, int version) => _documents[uri] = new CsprojDocument(uri, text, version);

  public void CloseDocument(Uri uri) => _documents.TryRemove(uri, out _);

  public string? GetDocumentContent(Uri uri) => _documents.TryGetValue(uri, out var doc) ? doc.Text : null;

  public int GetDocumentVersion(Uri uri) => _documents.TryGetValue(uri, out var doc) ? doc.Version : -1;

  public CsprojDocument? GetDocument(Uri uri) => _documents.TryGetValue(uri, out var doc) ? doc : null;

  public Uri? TryGetAnyDocumentUri() => _documents.Keys.FirstOrDefault();
}