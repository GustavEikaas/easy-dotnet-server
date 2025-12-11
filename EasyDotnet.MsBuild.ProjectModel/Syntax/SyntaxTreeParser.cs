using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

namespace EasyDotnet.MsBuild.ProjectModel.Syntax;

internal class SyntaxTreeParser(string text)
{

  public ProjectSyntax ParseProject(XElement projectElement)
  {
    var propertyGroups = ImmutableArray.CreateBuilder<PropertyGroupSyntax>();
    var itemGroups = ImmutableArray.CreateBuilder<ItemGroupSyntax>();
    var targets = ImmutableArray.CreateBuilder<TargetSyntax>();
    var imports = ImmutableArray.CreateBuilder<ImportSyntax>();

    foreach (var element in projectElement.Elements())
    {
      switch (element.Name.LocalName)
      {
        case "PropertyGroup":
          propertyGroups.Add(ParsePropertyGroup(element));
          break;
        case "ItemGroup":
          itemGroups.Add(ParseItemGroup(element));
          break;
        case "Target":
          targets.Add(ParseTarget(element));
          break;
        case "Import":
          imports.Add(ParseImport(element));
          break;
      }
    }

    var project = new ProjectSyntax
    {
      Kind = MsBuildSyntaxKind.Project,
      Sdk = projectElement.Attribute("Sdk")?.Value,
      Span = GetSpan(projectElement),
      PropertyGroups = propertyGroups.ToImmutable(),
      ItemGroups = itemGroups.ToImmutable(),
      Targets = targets.ToImmutable(),
      Imports = imports.ToImmutable()
    };

    SetParents(project);
    return project;
  }

  private PropertyGroupSyntax ParsePropertyGroup(XElement element)
  {
    var properties = element.Elements()
        .Select(ParseProperty)
        .ToImmutableArray();

    return new PropertyGroupSyntax
    {
      Kind = MsBuildSyntaxKind.PropertyGroup,
      Condition = element.Attribute("Condition")?.Value,
      Span = GetSpan(element),
      Properties = properties
    };
  }

  private PropertySyntax ParseProperty(XElement element) => new()
  {
    Kind = MsBuildSyntaxKind.Property,
    Name = element.Name.LocalName,
    Value = element.Value,
    Condition = element.Attribute("Condition")?.Value,
    Span = GetSpan(element)
  };

  private ItemGroupSyntax ParseItemGroup(XElement element)
  {
    var items = element.Elements()
        .Select(ParseItem)
        .ToImmutableArray();

    return new ItemGroupSyntax
    {
      Kind = MsBuildSyntaxKind.ItemGroup,
      Condition = element.Attribute("Condition")?.Value,
      Span = GetSpan(element),
      Items = items
    };
  }

  private ItemSyntax ParseItem(XElement element)
  {
    var metadata = element.Elements()
        .Select(e => new MetadataSyntax
        {
          Kind = MsBuildSyntaxKind.Metadata,
          Name = e.Name.LocalName,
          Value = e.Value,
          Span = GetSpan(e)
        })
        .ToImmutableArray();

    var kind = element.Name.LocalName switch
    {
      "PackageReference" => MsBuildSyntaxKind.PackageReference,
      "ProjectReference" => MsBuildSyntaxKind.ProjectReference,
      "Reference" => MsBuildSyntaxKind.Reference,
      "Compile" => MsBuildSyntaxKind.Compile,
      "Content" => MsBuildSyntaxKind.Content,
      "None" => MsBuildSyntaxKind.None_Item,
      _ => MsBuildSyntaxKind.Item
    };

    return new ItemSyntax
    {
      Kind = kind,
      ItemType = element.Name.LocalName,
      Include = element.Attribute("Include")?.Value ?? "",
      Span = GetSpan(element),
      Metadata = metadata
    };
  }

  private TargetSyntax ParseTarget(XElement element) => new()
  {
    Kind = MsBuildSyntaxKind.Target,
    Name = element.Attribute("Name")?.Value ?? "",
    BeforeTargets = element.Attribute("BeforeTargets")?.Value,
    AfterTargets = element.Attribute("AfterTargets")?.Value,
    Condition = element.Attribute("Condition")?.Value,
    Span = GetSpan(element)
  };

  private ImportSyntax ParseImport(XElement element) => new()
  {
    Kind = MsBuildSyntaxKind.Import,
    Project = element.Attribute("Project")?.Value ?? "",
    Sdk = element.Attribute("Sdk")?.Value,
    Condition = element.Attribute("Condition")?.Value,
    Span = GetSpan(element)
  };

  private TextSpan GetSpan(XElement element)
  {
    var lineInfo = (IXmlLineInfo)element;
    if (!lineInfo.HasLineInfo())
      return new TextSpan(0, 0);

    var lines = text.Split('\n');
    var start = 0;

    for (var i = 0; i < lineInfo.LineNumber - 1 && i < lines.Length; i++)
    {
      start += lines[i].Length + 1;
    }
    start += lineInfo.LinePosition - 1;

    var length = element.ToString().Length;
    return new TextSpan(start, length);
  }

  private static void SetParents(MsBuildSyntaxNode node)
  {
    foreach (var child in node.Children)
    {
      child.Parent = node;
      SetParents(child);
    }
  }
}