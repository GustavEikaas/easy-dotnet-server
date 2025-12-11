using System.Collections.Immutable;
using System.Text;

namespace EasyDotnet.MsBuild.ProjectModel.Syntax;

/// <summary>
/// Base for syntax nodes that haven't been materialized with spans yet.
/// These are "dirty" nodes that need to be serialized and reparsed to get accurate spans.
/// </summary>
public abstract record MsBuildSyntaxNodeDraft
{
  public abstract string ToXml(int indentLevel = 0);
}

public record PropertySyntaxDraft : MsBuildSyntaxNodeDraft
{
  public required string Name { get; init; }
  public required string Value { get; init; }
  public string? Condition { get; init; }

  public override string ToXml(int indentLevel = 0)
  {
    var sb = new StringBuilder();
    sb.Append(new string(' ', indentLevel * 2));
    sb.Append($"<{Name}");
    if (Condition != null) sb.Append($" Condition=\"{XmlEscape(Condition)}\"");
    sb.Append($">{XmlEscape(Value)}</{Name}>");
    sb.AppendLine();
    return sb.ToString();
  }

  private static string XmlEscape(string value) => value
      .Replace("&", "&amp;")
      .Replace("<", "&lt;")
      .Replace(">", "&gt;")
      .Replace("\"", "&quot;")
      .Replace("'", "&apos;");
}

public record PropertyGroupSyntaxDraft : MsBuildSyntaxNodeDraft
{
  public string? Condition { get; init; }
  public ImmutableArray<PropertySyntaxDraft> Properties { get; init; } = [];

  public override string ToXml(int indentLevel = 0)
  {
    var sb = new StringBuilder();
    sb.Append(new string(' ', indentLevel * 2));
    sb.Append("<PropertyGroup");
    if (Condition != null) sb.Append($" Condition=\"{XmlEscape(Condition)}\"");
    sb.AppendLine(">");

    foreach (var prop in Properties)
      sb.Append(prop.ToXml(indentLevel + 1));

    sb.Append(new string(' ', indentLevel * 2));
    sb.AppendLine("</PropertyGroup>");
    return sb.ToString();
  }

  private static string XmlEscape(string value) => value
      .Replace("&", "&amp;")
      .Replace("\"", "&quot;");
}

public record MetadataSyntaxDraft : MsBuildSyntaxNodeDraft
{
  public required string Name { get; init; }
  public required string Value { get; init; }

  public override string ToXml(int indentLevel = 0)
  {
    var sb = new StringBuilder();
    sb.Append(new string(' ', indentLevel * 2));
    sb.AppendLine($"<{Name}>{XmlEscape(Value)}</{Name}>");
    return sb.ToString();
  }

  private static string XmlEscape(string value) => value
      .Replace("&", "&amp;")
      .Replace("<", "&lt;")
      .Replace(">", "&gt;");
}

public record ItemSyntaxDraft : MsBuildSyntaxNodeDraft
{
  public required string ItemType { get; init; }
  public string? Include { get; init; }
  public string? Exclude { get; init; }
  public string? Remove { get; init; }
  public string? Update { get; init; }
  public string? Condition { get; init; }
  public ImmutableArray<MetadataSyntaxDraft> Metadata { get; init; } = [];
  public ImmutableDictionary<string, string> InlineAttributes { get; init; } = ImmutableDictionary<string, string>.Empty;

  public override string ToXml(int indentLevel = 0)
  {
    var sb = new StringBuilder();
    sb.Append(new string(' ', indentLevel * 2));
    sb.Append($"<{ItemType}");
    if (Include != null) sb.Append($" Include=\"{XmlEscape(Include)}\"");
    if (Exclude != null) sb.Append($" Exclude=\"{XmlEscape(Exclude)}\"");
    if (Remove != null) sb.Append($" Remove=\"{XmlEscape(Remove)}\"");
    if (Update != null) sb.Append($" Update=\"{XmlEscape(Update)}\"");
    if (Condition != null) sb.Append($" Condition=\"{XmlEscape(Condition)}\"");

    foreach (var (name, value) in InlineAttributes)
    {
      sb.Append($" {name}=\"{XmlEscape(value)}\"");
    }

    if (Metadata.Length == 0)
    {
      sb.AppendLine(" />");
    }
    else
    {
      sb.AppendLine(">");
      foreach (var meta in Metadata)
        sb.Append(meta.ToXml(indentLevel + 1));
      sb.Append(new string(' ', indentLevel * 2));
      sb.AppendLine($"</{ItemType}>");
    }

    return sb.ToString();
  }

  private static string XmlEscape(string value) => value
      .Replace("&", "&amp;")
      .Replace("<", "&lt;")
      .Replace(">", "&gt;")
      .Replace("\"", "&quot;");
}

public record ItemGroupSyntaxDraft : MsBuildSyntaxNodeDraft
{
  public string? Condition { get; init; }
  public ImmutableArray<ItemSyntaxDraft> Items { get; init; } = [];

  public override string ToXml(int indentLevel = 0)
  {
    var sb = new StringBuilder();
    sb.Append(new string(' ', indentLevel * 2));
    sb.Append("<ItemGroup");
    if (Condition != null) sb.Append($" Condition=\"{XmlEscape(Condition)}\"");
    sb.AppendLine(">");

    foreach (var item in Items)
      sb.Append(item.ToXml(indentLevel + 1));

    sb.Append(new string(' ', indentLevel * 2));
    sb.AppendLine("</ItemGroup>");
    return sb.ToString();
  }

  private static string XmlEscape(string value) => value
      .Replace("&", "&amp;")
      .Replace("\"", "&quot;");
}

public record ProjectSyntaxDraft : MsBuildSyntaxNodeDraft
{
  public string? Sdk { get; init; }
  public ImmutableArray<PropertyGroupSyntaxDraft> PropertyGroups { get; init; } = [];
  public ImmutableArray<ItemGroupSyntaxDraft> ItemGroups { get; init; } = [];

  public override string ToXml(int indentLevel = 0)
  {
    var sb = new StringBuilder();
    sb.Append("<Project");
    if (Sdk != null) sb.Append($" Sdk=\"{XmlEscape(Sdk)}\"");
    sb.AppendLine(">");

    foreach (var pg in PropertyGroups)
      sb.Append(pg.ToXml(indentLevel + 1));

    foreach (var ig in ItemGroups)
      sb.Append(ig.ToXml(indentLevel + 1));

    sb.AppendLine("</Project>");
    return sb.ToString();
  }

  private static string XmlEscape(string value) => value
      .Replace("&", "&amp;")
      .Replace("\"", "&quot;");
}