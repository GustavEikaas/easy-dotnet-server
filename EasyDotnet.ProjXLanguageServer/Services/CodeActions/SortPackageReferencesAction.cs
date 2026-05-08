using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services.CodeActions;

internal static class SortPackageReferencesAction
{
  public static CodeAction? Build(CsprojDocument doc, int rangeStart, int rangeEnd)
  {
    var itemGroup = AstSearch.FindElementOverlapping(doc.Root, rangeStart, rangeEnd, "ItemGroup");
    if (itemGroup == null)
      return null;

    var refs = itemGroup.Elements
        .Where(e => string.Equals(e.Name, "PackageReference", StringComparison.Ordinal))
        .Select(e => new PkgRef(GetIncludeValue(e) ?? string.Empty, ((SyntaxNode)e).SpanStart, ((SyntaxNode)e).Width))
        .ToList();

    if (refs.Count < 2)
      return null;

    var sorted = refs.OrderBy(r => r.Include, StringComparer.OrdinalIgnoreCase).ToList();
    if (refs.Select(r => r.Include).SequenceEqual(sorted.Select(r => r.Include), StringComparer.Ordinal))
      return null;

    var edits = new TextEdit[refs.Count];
    for (var i = 0; i < refs.Count; i++)
    {
      var target = refs[i];
      var source = sorted[i];
      edits[i] = new TextEdit
      {
        Range = PositionUtils.ToRange(doc.LineOffsets, target.Start, target.Length),
        NewText = doc.Text.Substring(source.Start, source.Length),
      };
    }

    return CodeActionBuilder.Build(doc, "Sort PackageReferences alphabetically", CodeActionKind.RefactorRewrite, null, edits);
  }

  private static string? GetIncludeValue(IXmlElementSyntax element)
  {
    foreach (var attr in element.Attributes)
    {
      if (string.Equals(attr.Name, "Include", StringComparison.Ordinal))
        return attr.Value;
    }
    return null;
  }

  private readonly record struct PkgRef(string Include, int Start, int Length);
}