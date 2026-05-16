using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services;

public sealed record AddPackageReferenceEditRequest(
    string ProjectPath,
    string PackageId,
    string Version);

public interface IPackageReferenceEditPlanner
{
  Task<WorkspaceEdit> PlanAddPackageReferenceAsync(AddPackageReferenceEditRequest request, CancellationToken cancellationToken);
}

public sealed class PackageReferenceEditPlanner(
    IProjXWorkspaceHierarchyService hierarchyService,
    IProjXDocumentTextProvider textProvider) : IPackageReferenceEditPlanner
{
  public async Task<WorkspaceEdit> PlanAddPackageReferenceAsync(AddPackageReferenceEditRequest request, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(request.ProjectPath))
      throw new ArgumentException("ProjectPath is required.", nameof(request));
    if (string.IsNullOrWhiteSpace(request.PackageId))
      throw new ArgumentException("PackageId is required.", nameof(request));
    if (string.IsNullOrWhiteSpace(request.Version))
      throw new ArgumentException("Version is required.", nameof(request));

    var hierarchy = await hierarchyService.ResolveAsync(request.ProjectPath, cancellationToken);
    var projectUri = new Uri(hierarchy.ProjectPath);
    var changes = new Dictionary<string, TextEdit[]>();

    var centralPackagePath = hierarchy.ManagePackageVersionsCentrally
      && !string.IsNullOrWhiteSpace(hierarchy.DirectoryPackagesPropsPath)
      && textProvider.TryGetText(hierarchy.DirectoryPackagesPropsPath, out _)
        ? hierarchy.DirectoryPackagesPropsPath
        : null;

    var projectEdit = PlanProjectPackageReferenceEdit(
        hierarchy.ProjectPath,
        request.PackageId,
        centralPackagePath is null ? request.Version : null);
    changes[projectUri.ToString()] = [projectEdit];

    if (centralPackagePath is not null)
    {
      var centralEdit = PlanCentralPackageVersionEdit(
          centralPackagePath,
          request.PackageId,
          request.Version);
      changes[new Uri(centralPackagePath).ToString()] = [centralEdit];
    }

    return new WorkspaceEdit { Changes = changes };
  }

  private TextEdit PlanProjectPackageReferenceEdit(string projectPath, string packageId, string? version)
  {
    var doc = LoadDocument(projectPath);
    var existing = FindItem(doc.Root, "PackageReference", packageId);
    if (existing is not null)
    {
      return ReplaceElement(doc, existing, BuildPackageReference(existing, packageId, version));
    }

    return InsertItem(doc, "PackageReference", BuildNewPackageReference(packageId, version));
  }

  private TextEdit PlanCentralPackageVersionEdit(string path, string packageId, string version)
  {
    var doc = LoadDocument(path);
    var existing = FindItem(doc.Root, "PackageVersion", packageId);
    if (existing is not null)
    {
      return ReplaceElement(doc, existing, BuildPackageVersion(existing, packageId, version));
    }

    return InsertItem(doc, "PackageVersion", BuildNewPackageVersion(packageId, version));
  }

  private CsprojDocument LoadDocument(string path)
  {
    if (!textProvider.TryGetText(path, out var text))
    {
      throw new FileNotFoundException("MSBuild XML file not found.", path);
    }

    return new CsprojDocument(new Uri(Path.GetFullPath(path)), text, 0);
  }

  private static IXmlElementSyntax? FindItem(SyntaxNode root, string itemName, string packageId) =>
      AstSearch.Elements(root)
          .Where(e => string.Equals(e.Name, itemName, StringComparison.Ordinal))
          .LastOrDefault(e => string.Equals(GetIdentity(e), packageId, StringComparison.OrdinalIgnoreCase));

  private static string? GetIdentity(IXmlElementSyntax element) =>
      GetAttributeValue(element, "Include") ?? GetAttributeValue(element, "Update");

  private static TextEdit ReplaceElement(CsprojDocument doc, IXmlElementSyntax element, string replacement)
  {
    var node = (SyntaxNode)element;
    return new TextEdit
    {
      Range = PositionUtils.ToRange(doc.LineOffsets, node.Start, node.FullWidth),
      NewText = replacement,
    };
  }

  private static TextEdit InsertItem(CsprojDocument doc, string itemName, string itemText)
  {
    var newline = DetectNewline(doc.Text);
    var itemGroup = FindItemGroupForNewItem(doc.Root, itemName);
    if (itemGroup is not null)
    {
      var groupElement = (XmlElementSyntax)itemGroup;
      var endTag = groupElement.EndTag;
      if (endTag is not null)
      {
        var itemIndent = InferChildIndent(doc.Text, itemGroup) ?? InferElementIndent(doc.Text, (SyntaxNode)itemGroup) + "  ";
        return InsertAt(doc, endTag.Start, itemIndent + itemText + newline);
      }
    }

    var project = AstSearch.Elements(doc.Root).FirstOrDefault(e => string.Equals(e.Name, "Project", StringComparison.Ordinal));
    if (project is XmlElementSyntax projectElement && projectElement.EndTag is not null)
    {
      var indent = InferElementIndent(doc.Text, (SyntaxNode)project) + "  ";
      var childIndent = indent + "  ";
      var text = $"{indent}<ItemGroup>{newline}{childIndent}{itemText}{newline}{indent}</ItemGroup>{newline}";
      return InsertAt(doc, projectElement.EndTag.Start, text);
    }

    var appendText = doc.Text.EndsWith(newline, StringComparison.Ordinal)
      ? $"<ItemGroup>{newline}  {itemText}{newline}</ItemGroup>{newline}"
      : $"{newline}<ItemGroup>{newline}  {itemText}{newline}</ItemGroup>{newline}";
    return InsertAt(doc, doc.Text.Length, appendText);
  }

  private static TextEdit InsertAt(CsprojDocument doc, int offset, string text) => new()
  {
    Range = PositionUtils.ToRange(doc.LineOffsets, offset, 0),
    NewText = text,
  };

  private static IXmlElementSyntax? FindItemGroupForNewItem(SyntaxNode root, string itemName) =>
      string.Equals(itemName, "PackageVersion", StringComparison.Ordinal)
        ? AstSearch.Elements(root)
            .Where(e => string.Equals(e.Name, "ItemGroup", StringComparison.Ordinal))
            .FirstOrDefault(g => g.Elements.Any(e => string.Equals(e.Name, itemName, StringComparison.Ordinal)))
        : AstSearch.Elements(root)
            .Where(e => string.Equals(e.Name, "ItemGroup", StringComparison.Ordinal))
            .LastOrDefault(g =>
            {
              var children = g.Elements.ToArray();
              return children.Length > 0 && children.All(e => string.Equals(e.Name, itemName, StringComparison.Ordinal));
            });

  private static string BuildPackageReference(IXmlElementSyntax existing, string packageId, string? version)
  {
    var attrs = GetAttributes(existing);
    SetIdentity(attrs, packageId);
    attrs.RemoveAll(a => string.Equals(a.Name, "Version", StringComparison.OrdinalIgnoreCase));
    if (version is not null)
    {
      attrs.Add(new XmlAttr("Version", version));
    }
    return BuildSelfClosingElement("PackageReference", attrs);
  }

  private static string BuildPackageVersion(IXmlElementSyntax existing, string packageId, string version)
  {
    var attrs = GetAttributes(existing);
    SetIdentity(attrs, packageId);
    var versionAttr = attrs.FindIndex(a => string.Equals(a.Name, "Version", StringComparison.OrdinalIgnoreCase));
    if (versionAttr >= 0)
    {
      attrs[versionAttr] = attrs[versionAttr] with { Value = version };
    }
    else
    {
      attrs.Add(new XmlAttr("Version", version));
    }
    return BuildSelfClosingElement("PackageVersion", attrs);
  }

  private static string BuildNewPackageReference(string packageId, string? version) =>
      version is null
        ? $"""<PackageReference Include="{Escape(packageId)}" />"""
        : $"""<PackageReference Include="{Escape(packageId)}" Version="{Escape(version)}" />""";

  private static string BuildNewPackageVersion(string packageId, string version) =>
      $"""<PackageVersion Include="{Escape(packageId)}" Version="{Escape(version)}" />""";

  private static List<XmlAttr> GetAttributes(IXmlElementSyntax element) =>
      [.. element.Attributes
          .Where(a => !string.IsNullOrEmpty(a.Name))
          .Select(a => new XmlAttr(a.Name, a.Value ?? string.Empty))];

  private static void SetIdentity(List<XmlAttr> attrs, string packageId)
  {
    var includeIndex = attrs.FindIndex(a => string.Equals(a.Name, "Include", StringComparison.OrdinalIgnoreCase));
    var updateIndex = attrs.FindIndex(a => string.Equals(a.Name, "Update", StringComparison.OrdinalIgnoreCase));
    var index = includeIndex >= 0 ? includeIndex : updateIndex;

    if (index >= 0)
    {
      attrs[index] = attrs[index] with { Value = packageId };
      return;
    }

    attrs.Insert(0, new XmlAttr("Include", packageId));
  }

  private static string BuildSelfClosingElement(string name, IEnumerable<XmlAttr> attrs) =>
      $"<{name}{string.Concat(attrs.Select(a => $" {a.Name}=\"{Escape(a.Value)}\""))} />";

  private static string? GetAttributeValue(IXmlElementSyntax element, string name)
  {
    foreach (var attr in element.Attributes)
    {
      if (string.Equals(attr.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        return attr.Value;
      }
    }
    return null;
  }

  private static string? InferChildIndent(string text, IXmlElementSyntax itemGroup)
  {
    var firstChild = itemGroup.Elements.FirstOrDefault();
    return firstChild is null ? null : InferElementIndent(text, (SyntaxNode)firstChild);
  }

  private static string InferElementIndent(string text, SyntaxNode node)
  {
    var lineStart = text.LastIndexOf('\n', Math.Max(0, node.Start - 1));
    lineStart = lineStart < 0 ? 0 : lineStart + 1;
    var i = lineStart;
    while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
    {
      i++;
    }
    return text[lineStart..i];
  }

  private static string DetectNewline(string text) =>
      text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

  private static string Escape(string value) =>
      value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

  private readonly record struct XmlAttr(string Name, string Value);
}
