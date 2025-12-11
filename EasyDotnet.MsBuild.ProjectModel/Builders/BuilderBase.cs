using System.Text;

namespace EasyDotnet.MsBuild.ProjectModel.Builders;

public abstract class BuilderBase
{
  protected static void AppendIndent(StringBuilder sb, int level) => sb.Append(new string(' ', level * 2));

  protected static void AppendAttribute(StringBuilder sb, string name, string? value)
  {
    if (!string.IsNullOrEmpty(value))
    {
      sb.Append($" {name}=\"{XmlEscape(value)}\"");
    }
  }

  protected static string XmlEscape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");
}