using LspPosition = Microsoft.VisualStudio.LanguageServer.Protocol.Position;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Utils;

public static class PositionUtils
{
  public static int[] BuildLineOffsets(string text)
  {
    var offsets = new List<int> { 0 };
    for (var i = 0; i < text.Length; i++)
    {
      if (text[i] == '\n')
      {
        offsets.Add(i + 1);
      }
    }
    return [.. offsets];
  }

  public static int ToOffset(int[] lineOffsets, int textLength, int line, int character)
  {
    if (line < 0)
    {
      return 0;
    }

    if (line >= lineOffsets.Length)
    {
      return textLength;
    }

    var offset = lineOffsets[line] + character;
    return Math.Min(offset, textLength);
  }

  public static LspPosition ToPosition(int[] lineOffsets, int offset)
  {
    var line = LineForOffset(lineOffsets, offset);
    var character = offset - lineOffsets[line];
    return new LspPosition { Line = line, Character = character };
  }

  public static LspRange ToRange(int[] lineOffsets, int start, int length) => new()
  {
    Start = ToPosition(lineOffsets, start),
    End = ToPosition(lineOffsets, start + length)
  };

  private static int LineForOffset(int[] lineOffsets, int offset)
  {
    var lo = 0;
    var hi = lineOffsets.Length - 1;
    while (lo < hi)
    {
      var mid = (lo + hi + 1) / 2;
      if (lineOffsets[mid] <= offset)
      {
        lo = mid;
      }
      else
      {
        hi = mid - 1;
      }
    }
    return lo;
  }
}