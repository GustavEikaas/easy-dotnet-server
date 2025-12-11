using System.Text;
using EasyDotnet.MsBuild.ProjectModel.Syntax;
using EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

namespace EasyDotnet.MsBuild.ProjectModel.Builders;

public class ProjectBuilder : BuilderBase
{
  private string? _sdk;
  private readonly List<PropertyGroupBuilder> _propertyGroups = [];
  private readonly List<ItemGroupBuilder> _itemGroups = [];
  private readonly List<TargetBuilder> _targets = [];
  private readonly List<ImportBuilder> _imports = [];

  public ProjectBuilder WithSdk(string sdk)
  {
    _sdk = sdk;
    return this;
  }

  public ProjectBuilder AddPropertyGroup(Action<PropertyGroupBuilder> configure)
  {
    var builder = new PropertyGroupBuilder();
    configure(builder);
    _propertyGroups.Add(builder);
    return this;
  }

  public ProjectBuilder AddItemGroup(Action<ItemGroupBuilder> configure)
  {
    var builder = new ItemGroupBuilder();
    configure(builder);
    _itemGroups.Add(builder);
    return this;
  }

  public ProjectBuilder AddTarget(Action<TargetBuilder> configure)
  {
    var builder = new TargetBuilder();
    configure(builder);
    _targets.Add(builder);
    return this;
  }

  public ProjectBuilder AddImport(Action<ImportBuilder> configure)
  {
    var builder = new ImportBuilder();
    configure(builder);
    _imports.Add(builder);
    return this;
  }

  public ProjectBuilder AddImport(string project) => AddImport(import => import.WithProject(project));

  public string ToXml()
  {
    var sb = new StringBuilder();
    sb.Append("<Project");
    AppendAttribute(sb, "Sdk", _sdk);
    sb.AppendLine(">");

    foreach (var propertyGroup in _propertyGroups)
    {
      propertyGroup.AppendXml(sb, 1);
    }

    foreach (var itemGroup in _itemGroups)
    {
      itemGroup.AppendXml(sb, 1);
    }

    foreach (var import in _imports)
    {
      import.AppendXml(sb, 1);
    }

    foreach (var target in _targets)
    {
      target.AppendXml(sb, 1);
    }

    sb.AppendLine("</Project>");
    return sb.ToString();
  }

  public ProjectSyntax Build()
  {
    var xml = ToXml();
    var tree = MsBuildSyntaxTree.Parse(xml);
    return tree.Root;
  }
}