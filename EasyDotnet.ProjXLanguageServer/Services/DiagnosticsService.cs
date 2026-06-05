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
  public const string DuplicatePackageReference = "projx-duplicate-packageref";
  public const string SingleTfmInTargetFrameworks = "projx-single-tfm-in-targetframeworks";
  public const string ConflictingTargetFrameworkProperties = "projx-conflicting-targetframework-properties";
  public const string MismatchedTagNames = "projx-mismatched-tag-names";
}

public class DiagnosticsService(IFileSystem fileSystem) : IDiagnosticsService
{
  public Diagnostic[] GetDiagnostics(CsprojDocument doc)
  {
    var diagnostics = new List<Diagnostic>();
    var docDir = fileSystem.Path.GetDirectoryName(doc.Uri.LocalPath);
    if (string.IsNullOrEmpty(docDir))
      return [];

    AddDuplicatePackageReferenceDiagnostics(doc, diagnostics);
    AddSingleTfmInTargetFrameworksDiagnostics(doc, diagnostics);
    AddConflictingTargetFrameworkDiagnostics(doc, diagnostics);
    AddMismatchedTagDiagnostics(doc, diagnostics);

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

      var normalized = NormalizeSeparators(include);
      var resolved = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(docDir, normalized));
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

  private static string NormalizeSeparators(string path) =>
      Path.DirectorySeparatorChar == '/' ? path.Replace('\\', '/') : path.Replace('/', '\\');

  private static void AddDuplicatePackageReferenceDiagnostics(CsprojDocument doc, List<Diagnostic> diagnostics)
  {
    var seen = new HashSet<(string Include, string Condition)>();
    foreach (var element in EnumerateElements(doc.Root))
    {
      if (!string.Equals(element.Name, "PackageReference", StringComparison.Ordinal))
        continue;

      var includeAttr = GetIncludeAttribute(element);
      if (includeAttr?.ValueNode == null)
        continue;

      var include = includeAttr.Value;
      if (string.IsNullOrWhiteSpace(include))
        continue;

      if (!seen.Add((include.ToLowerInvariant(), GetConditionSignature(element))))
      {
        var node = (SyntaxNode)element;
        diagnostics.Add(new Diagnostic
        {
          Range = PositionUtils.ToRange(doc.LineOffsets, node.SpanStart, node.Width),
          Severity = LspDiagnosticSeverity.Error,
          Source = DiagnosticCodes.Source,
          Code = DiagnosticCodes.DuplicatePackageReference,
          Message = $"Duplicate PackageReference: '{include}'"
        });
      }
    }
  }

  private static void AddSingleTfmInTargetFrameworksDiagnostics(CsprojDocument doc, List<Diagnostic> diagnostics)
  {
    foreach (var element in EnumerateElements(doc.Root))
    {
      if (!string.Equals(element.Name, "TargetFrameworks", StringComparison.Ordinal))
        continue;

      if (element is not XmlElementSyntax {StartTag: {} start, EndTag: {} end})
        continue;

      var contentStart = start.Start + start.FullWidth;
      var contentEnd = end.Start;
      if (contentEnd <= contentStart || contentEnd > doc.Text.Length)
        continue;

      // <TargetFrameworks> is a ';'-separated list. After trimming whitespace and any
      // leading/trailing ';', a remaining ';' means there is more than one TFM.
      var singleTfm = doc.Text.AsSpan(contentStart, contentEnd - contentStart).Trim().Trim(';').Trim();
      if (singleTfm.IsEmpty || singleTfm.Contains(';'))
        continue;

      var node = (SyntaxNode)element;
      diagnostics.Add(new Diagnostic
      {
        Range = PositionUtils.ToRange(doc.LineOffsets, node.SpanStart, node.Width),
        Severity = LspDiagnosticSeverity.Warning,
        Source = DiagnosticCodes.Source,
        Code = DiagnosticCodes.SingleTfmInTargetFrameworks,
        Message = $"<TargetFrameworks> contains a single TFM '{singleTfm}'; use <TargetFramework> instead.",
      });
    }
  }

  private static void AddConflictingTargetFrameworkDiagnostics(CsprojDocument doc, List<Diagnostic> diagnostics)
  {
    var unconditionalSingles = new List<IXmlElementSyntax>();
    var unconditionalMultis = new List<IXmlElementSyntax>();

    foreach (var element in EnumerateElements(doc.Root))
    {
      var name = element.Name;
      if (!string.Equals(name, "TargetFramework", StringComparison.Ordinal)
          && !string.Equals(name, "TargetFrameworks", StringComparison.Ordinal))
        continue;

      if (HasConditionOnSelfOrAncestor(element))
        continue;

      if (string.Equals(name, "TargetFramework", StringComparison.Ordinal))
      {
        if (GetElementValue(doc, element).IsWhiteSpace())
          continue;
        unconditionalSingles.Add(element);
      }
      else
        unconditionalMultis.Add(element);
    }

    if (unconditionalSingles.Count == 0 || unconditionalMultis.Count == 0)
      return;

    foreach (var element in unconditionalSingles.Concat(unconditionalMultis))
    {
      var node = (SyntaxNode)element;
      diagnostics.Add(new Diagnostic
      {
        Range = PositionUtils.ToRange(doc.LineOffsets, node.SpanStart, node.Width),
        Severity = LspDiagnosticSeverity.Error,
        Source = DiagnosticCodes.Source,
        Code = DiagnosticCodes.ConflictingTargetFrameworkProperties,
        Message = "Cannot specify both <TargetFramework> and <TargetFrameworks>; <TargetFramework> takes precedence.",
      });
    }
  }

  private static void AddMismatchedTagDiagnostics(CsprojDocument doc, List<Diagnostic> diagnostics)
  {
    foreach (var element in EnumerateElements(doc.Root))
    {
      if (element is not XmlElementSyntax full)
        continue;

      var startName = full.StartTag?.Name;
      var endName = full.EndTag?.Name;
      if (string.IsNullOrEmpty(startName) || string.IsNullOrEmpty(endName))
        continue;
      if (string.Equals(startName, endName, StringComparison.Ordinal))
        continue;

      var endNameNode = full.EndTag!.NameNode?.LocalNameNode;
      if (endNameNode == null)
        continue;

      diagnostics.Add(new Diagnostic
      {
        Range = PositionUtils.ToRange(doc.LineOffsets, endNameNode.Start, endNameNode.FullWidth),
        Severity = LspDiagnosticSeverity.Error,
        Source = DiagnosticCodes.Source,
        Code = DiagnosticCodes.MismatchedTagNames,
        Message = $"Closing tag '</{endName}>' does not match opening tag '<{startName}>'.",
      });
    }
  }

  private static string GetConditionSignature(IXmlElementSyntax element)
  {
    var conditions = new List<string>();
    var current = element;
    while (current != null)
    {
      foreach (var attr in current.AttributesNode)
      {
        if (string.Equals(attr.Name, "Condition", StringComparison.OrdinalIgnoreCase))
          conditions.Add(attr.Value.Trim());
      }
      current = current.Parent;
    }
    return string.Join(" && ", conditions);
  }

  private static ReadOnlySpan<char> GetElementValue(CsprojDocument doc, IXmlElementSyntax element)
  {
    if (element is not XmlElementSyntax { StartTag: {} start, EndTag: {} end})
      return default;

    var contentStart = start.Start + start.FullWidth;
    var contentEnd = end.Start;
    return contentEnd <= contentStart || contentEnd > doc.Text.Length
      ? default
      : doc.Text.AsSpan(contentStart, contentEnd - contentStart);
  }

  private static bool HasConditionOnSelfOrAncestor(IXmlElementSyntax element)
  {
    var current = element;
    while (current != null)
    {
      foreach (var attr in current.AttributesNode)
      {
        if (string.Equals(attr.Name, "Condition", StringComparison.OrdinalIgnoreCase))
          return true;
      }
      current = current.Parent;
    }
    return false;
  }

  private static IEnumerable<IXmlElementSyntax> EnumerateElements(SyntaxNode root)
  {
    if (root is IXmlElementSyntax self)
      yield return self;
    foreach (var child in root.ChildNodes)
    {
      foreach (var descendant in EnumerateElements(child))
        yield return descendant;
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