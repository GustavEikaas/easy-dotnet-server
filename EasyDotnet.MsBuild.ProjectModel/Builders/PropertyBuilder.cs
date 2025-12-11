using System.Text;

namespace EasyDotnet.MsBuild.ProjectModel.Builders;

public class PropertyBuilder : BuilderBase
{
  private string _name = "";
  private string _value = "";
  private string? _condition;

  public PropertyBuilder WithName(string name)
  {
    _name = name;
    return this;
  }

  public PropertyBuilder WithValue(string value)
  {
    _value = value;
    return this;
  }

  public PropertyBuilder WithCondition(string condition)
  {
    _condition = condition;
    return this;
  }

  internal void AppendXml(StringBuilder sb, int indentLevel)
  {
    AppendIndent(sb, indentLevel);
    sb.Append($"<{_name}");
    AppendAttribute(sb, "Condition", _condition);
    sb.Append($">{XmlEscape(_value)}</{_name}>");
    sb.AppendLine();
  }
}