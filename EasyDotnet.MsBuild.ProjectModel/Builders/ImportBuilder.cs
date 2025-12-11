using System.Text;

namespace EasyDotnet.MsBuild.ProjectModel.Builders;

public class ImportBuilder : BuilderBase
{
  private string _project = "";
  private string? _sdk;
  private string? _condition;

  public ImportBuilder WithProject(string project)
  {
    _project = project;
    return this;
  }

  public ImportBuilder WithSdk(string sdk)
  {
    _sdk = sdk;
    return this;
  }

  public ImportBuilder WithCondition(string condition)
  {
    _condition = condition;
    return this;
  }

  internal void AppendXml(StringBuilder sb, int indentLevel)
  {
    AppendIndent(sb, indentLevel);
    sb.Append("<Import");
    AppendAttribute(sb, "Project", _project);
    AppendAttribute(sb, "Sdk", _sdk);
    AppendAttribute(sb, "Condition", _condition);
    sb.AppendLine(" />");
  }
}