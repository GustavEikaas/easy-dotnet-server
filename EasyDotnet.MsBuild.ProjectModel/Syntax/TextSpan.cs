using System.Xml;
using System.Xml.Linq;

namespace EasyDotnet.MsBuild.ProjectModel.Syntax;

public readonly record struct TextSpan(int Start, int Length)
{
  public int End => Start + Length;

  public bool Contains(int position) => position >= Start && position < End;
  public bool Contains(TextSpan span) => span.Start >= Start && span.End <= End;
  public bool Overlaps(TextSpan span) => Start < span.End && End > span.Start;
}

public readonly record struct Position(int Line, int Character)
{
  public static Position FromXmlLineInfo(IXmlLineInfo lineInfo) => new(lineInfo.LineNumber - 1, lineInfo.LinePosition - 1);
}

public readonly record struct Range(Position Start, Position End)
{
  //TODO: why is text unused
  public static Range FromElement(XElement element, string text)
  {
    var lineInfo = (IXmlLineInfo)element;
    if (!lineInfo.HasLineInfo())
      return new Range(new Position(0, 0), new Position(0, 1));

    var start = new Position(lineInfo.LineNumber - 1, lineInfo.LinePosition - 1);
    var end = new Position(lineInfo.LineNumber - 1, lineInfo.LinePosition + element.Name.LocalName.Length + 2);
    return new Range(start, end);
  }
}