using System.Text;
using EasyDotnet.MsBuild.ProjectModel.Syntax;

namespace EasyDotnet.MsBuild.ProjectModel.Builders;

public class ItemGroupBuilder : BuilderBase
{
  private string? _condition;
  private readonly List<ItemBuilder> _items = [];

  public ItemGroupBuilder WithCondition(string condition)
  {
    _condition = condition;
    return this;
  }

  public ItemGroupBuilder AddItem(string itemType, string include)
  {
    var item = new ItemBuilder()
        .WithItemType(itemType)
        .WithInclude(include);
    _items.Add(item);
    return this;
  }

  public ItemGroupBuilder AddItem(Action<ItemBuilder> configure)
  {
    var builder = new ItemBuilder();
    configure(builder);
    _items.Add(builder);
    return this;
  }

  public ItemGroupBuilder AddPackageReference(string packageName, string version) => AddItem(item => item
                                                                                          .WithItemType("PackageReference")
                                                                                          .WithInclude(packageName)
                                                                                          .AddMetadata("Version", version));

  public ItemGroupBuilder AddProjectReference(string projectPath) => AddItem(item => item
                                                                          .WithItemType("ProjectReference")
                                                                          .WithInclude(projectPath));

  public ItemGroupBuilder AddCompile(string filePath) => AddItem(item => item
                                                              .WithItemType("Compile")
                                                              .WithInclude(filePath));

  public ItemGroupBuilder AddContent(string filePath, string? copyToOutputDirectory = null) => AddItem(item =>
                                                                                                {
                                                                                                  item.WithItemType("Content").WithInclude(filePath);
                                                                                                  if (copyToOutputDirectory != null)
                                                                                                  {
                                                                                                    item.AddMetadata("CopyToOutputDirectory", copyToOutputDirectory);
                                                                                                  }
                                                                                                });
  public ItemGroupSyntaxDraft BuildDraft() => new()
  {
    Condition = _condition,
    Items = [.. _items.Select(i => i.BuildDraft())]
  };

  internal void AppendXml(StringBuilder sb, int indentLevel)
  {
    AppendIndent(sb, indentLevel);
    sb.Append("<ItemGroup");
    AppendAttribute(sb, "Condition", _condition);
    sb.AppendLine(">");

    foreach (var item in _items)
    {
      item.AppendXml(sb, indentLevel + 1);
    }

    AppendIndent(sb, indentLevel);
    sb.AppendLine("</ItemGroup>");
  }
}