using System.IO.Abstractions;
using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspDiagnosticSeverity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IDiagnosticsService
{
  Diagnostic[] GetDiagnostics(CsprojDocument doc);
}

public static class DiagnosticCodes
{
  public const string Source = "projx";
  public const string MissingProjectReference = "projx-missing-projectref";
}

public class DiagnosticsService(IFileSystem fileSystem) : IDiagnosticsService
{
  public Diagnostic[] GetDiagnostics(CsprojDocument doc)
  {
    var diagnostics = new List<Diagnostic>();
    var docDir = fileSystem.Path.GetDirectoryName(doc.Uri.LocalPath);
    if (string.IsNullOrEmpty(docDir))
      return [];

    foreach (var element in EnumerateElements(doc.Root))
    {
      if (!string.Equals(element.Name, "ProjectReference", StringComparison.Ordinal))
        continue;

      var includeAttr = GetIncludeAttribute(element);
      if (includeAttr?.ValueNode == null)
        continue;

      var include = includeAttr.Value;
      if (string.IsNullOrWhiteSpace(include))
        continue;

      var resolved = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(docDir, include));
      if (fileSystem.File.Exists(resolved))
        continue;

      var node = (SyntaxNode)element;
      var range = PositionUtils.ToRange(doc.LineOffsets, node.SpanStart, node.Width);

      diagnostics.Add(new Diagnostic
      {
        Range = range,
        Severity = LspDiagnosticSeverity.Error,
        Source = DiagnosticCodes.Source,
        Code = DiagnosticCodes.MissingProjectReference,
        Message = $"Project reference path not found: '{include}'"
      });
    }

    return [.. diagnostics];
  }

  private static IEnumerable<IXmlElementSyntax> EnumerateElements(SyntaxNode root)
  {
    var stack = new Stack<SyntaxNode>();
    stack.Push(root);
    while (stack.Count > 0)
    {
      var node = stack.Pop();
      if (node is IXmlElementSyntax element)
        yield return element;
      foreach (var child in node.ChildNodes)
        stack.Push(child);
    }
  }

  private static XmlAttributeSyntax? GetIncludeAttribute(IXmlElementSyntax element)
  {
    foreach (var attr in element.AttributesNode)
    {
      if (string.Equals(attr.Name, "Include", StringComparison.Ordinal))
        return attr;
    }
    return null;
  }
}
