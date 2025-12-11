using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IDiagnosticsService
{
  Diagnostic[] AnalyzeDocument(string xml);
}

public class DiagnosticsService : IDiagnosticsService
{
  public const string EmptyGroupDiagnosticCode = "ProjX001";

  public Diagnostic[] AnalyzeDocument(string xml)
  {
    var diagnostics = new List<Diagnostic>();

    try
    {
      var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

      foreach (var element in doc.Descendants())
      {
        // Check for empty ItemGroup or PropertyGroup
        if ((element.Name.LocalName == "ItemGroup" || element.Name.LocalName == "PropertyGroup")
            && !element.HasElements
            && string.IsNullOrWhiteSpace(element.Value))
        {
          var range = GetRangeForElement(element, xml);
          diagnostics.Add(new Diagnostic
          {
            Range = range,
            Severity = DiagnosticSeverity.Hint,
            Code = EmptyGroupDiagnosticCode,
            Source = "ProjX",
            Message = $"Empty {element.Name.LocalName} can be removed",
            Tags = [DiagnosticTag.Unnecessary]
          });
        }
      }
    }
    catch
    {
      // Ignore parse errors for diagnostics
    }

    return [.. diagnostics];
  }

  private Microsoft.VisualStudio.LanguageServer.Protocol.Range GetRangeForElement(XElement element, string xml)
  {
    var lineInfo = (IXmlLineInfo)element;
    if (!lineInfo.HasLineInfo())
    {
      return new Microsoft.VisualStudio.LanguageServer.Protocol.Range
      {
        Start = new Position(0, 0),
        End = new Position(0, 0)
      };
    }

    var startLine = lineInfo.LineNumber - 1;
    var startChar = lineInfo.LinePosition - 2; // Adjust for < character

    // Find the end of the element
    var lines = xml.Split('\n');
    var endLine = startLine;
    var endChar = startChar;

    // Simple heuristic: find the closing tag on the same or following lines
    var elementString = element.ToString();
    var elementLines = elementString.Split('\n');

    if (elementLines.Length == 1)
    {
      // Single line element
      endChar = startChar + elementString.Length;
    }
    else
    {
      // Multi-line element
      endLine = startLine + elementLines.Length - 1;
      endChar = elementLines[^1].Length;
    }

    return new Microsoft.VisualStudio.LanguageServer.Protocol.Range
    {
      Start = new Position(startLine, Math.Max(0, startChar)),
      End = new Position(endLine, endChar)
    };
  }
}