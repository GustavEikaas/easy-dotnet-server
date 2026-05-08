using EasyDotnet.ProjXLanguageServer.Services;

namespace EasyDotnet.ProjXLanguageServer.Tests.Helpers;

public static class Docs
{
  public static CsprojDocument Make(string text, string? path = null)
  {
    var uri = new Uri(path ?? "/tmp/test/Sample.csproj");
    return new CsprojDocument(uri, text, 1);
  }

  public static (int line, int character) PositionAt(string text, string marker)
  {
    var idx = text.IndexOf(marker, StringComparison.Ordinal);
    if (idx < 0)
      throw new ArgumentException($"Marker '{marker}' not found in text");
    var line = 0;
    var lastNl = -1;
    for (var i = 0; i < idx; i++)
    {
      if (text[i] == '\n')
      {
        line++;
        lastNl = i;
      }
    }
    return (line, idx - lastNl - 1);
  }
}