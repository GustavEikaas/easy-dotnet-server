using System.Text;
using EasyDotnet.MsBuild.ProjectModel.Syntax;
using EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

namespace EasyDotnet.MsBuild.ProjectModel.Extensions;

public static class SyntaxNodeExtensions
{
  public static string ToXml(this ProjectSyntax project)
  {
    var sb = new StringBuilder();
    sb.Append("<Project");
    if (project.Sdk != null)
    {
      sb.Append($" Sdk=\"{project.Sdk}\"");
    }
    sb.AppendLine(">");

    foreach (var propertyGroup in project.PropertyGroups)
    {
      sb.Append(propertyGroup.ToXml(1));
    }

    foreach (var itemGroup in project.ItemGroups)
    {
      sb.Append(itemGroup.ToXml(1));
    }

    foreach (var import in project.Imports)
    {
      sb.Append(import.ToXml(1));
    }

    foreach (var target in project.Targets)
    {
      sb.Append(target.ToXml(1));
    }

    sb.AppendLine("</Project>");
    return sb.ToString();
  }

  public static string ToXml(this PropertyGroupSyntax propertyGroup, int indentLevel = 0)
  {
    var sb = new StringBuilder();
    AppendIndent(sb, indentLevel);
    sb.Append("<PropertyGroup");
    if (propertyGroup.Condition != null)
    {
      sb.Append($" Condition=\"{propertyGroup.Condition}\"");
    }
    sb.AppendLine(">");

    foreach (var property in propertyGroup.Properties)
    {
      sb.Append(property.ToXml(indentLevel + 1));
    }

    AppendIndent(sb, indentLevel);
    sb.AppendLine("</PropertyGroup>");
    return sb.ToString();
  }

  public static string ToXml(this PropertySyntax property, int indentLevel = 0)
  {
    var sb = new StringBuilder();
    AppendIndent(sb, indentLevel);
    sb.Append($"<{property.Name}");
    if (property.Condition != null)
    {
      sb.Append($" Condition=\"{property.Condition}\"");
    }
    sb.Append($">{XmlEscape(property.Value)}</{property.Name}>");
    sb.AppendLine();
    return sb.ToString();
  }

  public static string ToXml(this ItemGroupSyntax itemGroup, int indentLevel = 0)
  {
    var sb = new StringBuilder();
    AppendIndent(sb, indentLevel);
    sb.Append("<ItemGroup");
    if (itemGroup.Condition != null)
    {
      sb.Append($" Condition=\"{itemGroup.Condition}\"");
    }
    sb.AppendLine(">");

    foreach (var item in itemGroup.Items)
    {
      sb.Append(item.ToXml(indentLevel + 1));
    }

    AppendIndent(sb, indentLevel);
    sb.AppendLine("</ItemGroup>");
    return sb.ToString();
  }

  public static string ToXml(this ItemSyntax item, int indentLevel = 0)
  {
    var sb = new StringBuilder();
    AppendIndent(sb, indentLevel);
    sb.Append($"<{item.ItemType}");
    if (!string.IsNullOrEmpty(item.Include))
    {
      sb.Append($" Include=\"{item.Include}\"");
    }

    if (item.Metadata.Length == 0)
    {
      sb.AppendLine(" />");
    }
    else
    {
      sb.AppendLine(">");
      foreach (var metadata in item.Metadata)
      {
        sb.Append(metadata.ToXml(indentLevel + 1));
      }
      AppendIndent(sb, indentLevel);
      sb.AppendLine($"</{item.ItemType}>");
    }

    return sb.ToString();
  }

  public static string ToXml(this MetadataSyntax metadata, int indentLevel = 0)
  {
    var sb = new StringBuilder();
    AppendIndent(sb, indentLevel);
    sb.AppendLine($"<{metadata.Name}>{XmlEscape(metadata.Value)}</{metadata.Name}>");
    return sb.ToString();
  }

  public static string ToXml(this TargetSyntax target, int indentLevel = 0)
  {
    var sb = new StringBuilder();
    AppendIndent(sb, indentLevel);
    sb.Append("<Target");
    sb.Append($" Name=\"{target.Name}\"");
    if (target.BeforeTargets != null)
    {
      sb.Append($" BeforeTargets=\"{target.BeforeTargets}\"");
    }
    if (target.AfterTargets != null)
    {
      sb.Append($" AfterTargets=\"{target.AfterTargets}\"");
    }
    if (target.Condition != null)
    {
      sb.Append($" Condition=\"{target.Condition}\"");
    }
    sb.AppendLine(" />");
    return sb.ToString();
  }

  public static string ToXml(this ImportSyntax import, int indentLevel = 0)
  {
    var sb = new StringBuilder();
    AppendIndent(sb, indentLevel);
    sb.Append("<Import");
    sb.Append($" Project=\"{import.Project}\"");
    if (import.Sdk != null)
    {
      sb.Append($" Sdk=\"{import.Sdk}\"");
    }
    if (import.Condition != null)
    {
      sb.Append($" Condition=\"{import.Condition}\"");
    }
    sb.AppendLine(" />");
    return sb.ToString();
  }

  private static void AppendIndent(StringBuilder sb, int level) => sb.Append(new string(' ', level * 2));

  private static string XmlEscape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");
}