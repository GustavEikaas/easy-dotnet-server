using System.IO.Abstractions;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IProjXDocumentTextProvider
{
  bool TryGetText(string path, out string text);
}

public sealed class ProjXDocumentTextProvider(
    IDocumentManager documentManager,
    IFileSystem fileSystem) : IProjXDocumentTextProvider
{
  public bool TryGetText(string path, out string text)
  {
    var fullPath = fileSystem.Path.GetFullPath(path);
    foreach (var doc in documentManager.GetOpenDocuments())
    {
      if (doc.Uri.IsFile
          && string.Equals(
            fileSystem.Path.GetFullPath(doc.Uri.LocalPath),
            fullPath,
            StringComparison.OrdinalIgnoreCase))
      {
        text = doc.Text;
        return true;
      }
    }

    if (fileSystem.File.Exists(fullPath))
    {
      text = fileSystem.File.ReadAllText(fullPath);
      return true;
    }

    text = string.Empty;
    return false;
  }
}