using System.Text;

namespace EasyDotnet.MsBuild.ProjectModel.Builders;

public class TargetBuilder : BuilderBase
{
  private string _name = "";
  private string? _beforeTargets;
  private string? _afterTargets;
  private string? _condition;

  public TargetBuilder WithName(string name)
  {
    _name = name;
    return this;
  }

  public TargetBuilder WithBeforeTargets(string beforeTargets)
  {
    _beforeTargets = beforeTargets;
    return this;
  }

  public TargetBuilder WithAfterTargets(string afterTargets)
  {
    _afterTargets = afterTargets;
    return this;
  }

  public TargetBuilder WithCondition(string condition)
  {
    _condition = condition;
    return this;
  }

  internal void AppendXml(StringBuilder sb, int indentLevel)
  {
    AppendIndent(sb, indentLevel);
    sb.Append("<Target");
    AppendAttribute(sb, "Name", _name);
    AppendAttribute(sb, "BeforeTargets", _beforeTargets);
    AppendAttribute(sb, "AfterTargets", _afterTargets);
    AppendAttribute(sb, "Condition", _condition);
    sb.AppendLine(" />");
  }
}