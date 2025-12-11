using System.Text;

namespace EasyDotnet.MsBuild.ProjectModel.Builders;

public class PropertyGroupBuilder : BuilderBase
{
  private string? _condition;
  private readonly List<PropertyBuilder> _properties = [];

  public PropertyGroupBuilder WithCondition(string condition)
  {
    _condition = condition;
    return this;
  }

  public PropertyGroupBuilder AddProperty(string name, string value)
  {
    var property = new PropertyBuilder()
        .WithName(name)
        .WithValue(value);
    _properties.Add(property);
    return this;
  }

  public PropertyGroupBuilder AddProperty(string name, string value, string condition)
  {
    var property = new PropertyBuilder()
        .WithName(name)
        .WithValue(value)
        .WithCondition(condition);
    _properties.Add(property);
    return this;
  }

  public PropertyGroupBuilder AddProperty(Action<PropertyBuilder> configure)
  {
    var builder = new PropertyBuilder();
    configure(builder);
    _properties.Add(builder);
    return this;
  }

  public PropertyGroupBuilder AddProperties(params (string name, string value)[] properties)
  {
    foreach (var (name, value) in properties)
    {
      AddProperty(name, value);
    }
    return this;
  }

  internal void AppendXml(StringBuilder sb, int indentLevel)
  {
    AppendIndent(sb, indentLevel);
    sb.Append("<PropertyGroup");
    AppendAttribute(sb, "Condition", _condition);
    sb.AppendLine(">");

    foreach (var property in _properties)
    {
      property.AppendXml(sb, indentLevel + 1);
    }

    AppendIndent(sb, indentLevel);
    sb.AppendLine("</PropertyGroup>");
  }
}