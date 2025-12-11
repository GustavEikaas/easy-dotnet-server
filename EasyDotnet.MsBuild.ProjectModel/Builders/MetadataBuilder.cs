using System.Text;
using EasyDotnet.MsBuild.ProjectModel.Syntax;

namespace EasyDotnet.MsBuild.ProjectModel.Builders;

public class MetadataBuilder : BuilderBase
{
  private string _value = "";

  public string Name { get; private set; } = "";

  public MetadataBuilder WithName(string name)
  {
    Name = name;
    return this;
  }

  public MetadataBuilder WithValue(string value)
  {
    _value = value;
    return this;
  }

  public MetadataSyntaxDraft BuildDraft() => new()
  {
    Name = Name,
    Value = _value
  };

  internal void AppendXml(StringBuilder sb, int indentLevel)
  {
    AppendIndent(sb, indentLevel);
    sb.AppendLine($"<{Name}>{XmlEscape(_value)}</{Name}>");
  }
}