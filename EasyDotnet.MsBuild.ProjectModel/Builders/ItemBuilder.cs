using System.Collections.Immutable;
using System.Text;
using EasyDotnet.MsBuild.ProjectModel.Syntax;
using EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

namespace EasyDotnet.MsBuild.ProjectModel.Builders;

public class ItemBuilder : BuilderBase
{
  private string _itemType = "";
  private string _include = "";
  private string? _exclude;
  private string? _remove;
  private string? _update;
  private string? _condition;
  private readonly List<MetadataBuilder> _metadata = [];
  private readonly Dictionary<string, string> _inlineAttributes = []; // New

  public ItemBuilder WithAttribute(string name, string value)
  {
    _inlineAttributes[name] = value;
    return this;
  }

  public ItemBuilder WithItemType(string itemType)
  {
    _itemType = itemType;
    return this;
  }

  public ItemBuilder WithInclude(string include)
  {
    _include = include;
    return this;
  }

  public ItemBuilder WithExclude(string exclude)
  {
    _exclude = exclude;
    return this;
  }

  public ItemBuilder WithRemove(string remove)
  {
    _remove = remove;
    return this;
  }

  public ItemBuilder WithUpdate(string update)
  {
    _update = update;
    return this;
  }

  public ItemBuilder WithCondition(string condition)
  {
    _condition = condition;
    return this;
  }

  public ItemBuilder AddMetadata(string name, string value)
  {
    var metadata = new MetadataBuilder()
        .WithName(name)
        .WithValue(value);
    _metadata.Add(metadata);
    return this;
  }

  public ItemSyntaxDraft BuildDraft()
  {
    var metadata = _metadata
        .Where(m => !_inlineAttributes.ContainsKey(m.Name))
        .Select(m => m.BuildDraft())
        .ToImmutableArray();

    return new ItemSyntaxDraft
    {
      ItemType = _itemType,
      Include = _include,
      Exclude = _exclude,
      Remove = _remove,
      Update = _update,
      Condition = _condition,
      Metadata = metadata,
      InlineAttributes = _inlineAttributes.ToImmutableDictionary()
    };
  }

  public ItemBuilder AddMetadata(Action<MetadataBuilder> configure)
  {
    var builder = new MetadataBuilder();
    configure(builder);
    _metadata.Add(builder);
    return this;
  }
  public ItemSyntax BuildSyntax()
  {
    var xml = new StringBuilder();
    AppendXml(xml, 0);

    var parsed = MsBuildSyntaxTree.Parse($"<Project><ItemGroup>{xml}</ItemGroup></Project>");
    return parsed.Root.ItemGroups[0].Items[0];
  }

  internal void AppendXml(StringBuilder sb, int indentLevel)
  {
    AppendIndent(sb, indentLevel);
    sb.Append($"<{_itemType}");
    AppendAttribute(sb, "Include", _include);
    AppendAttribute(sb, "Exclude", _exclude);
    AppendAttribute(sb, "Remove", _remove);
    AppendAttribute(sb, "Update", _update);
    AppendAttribute(sb, "Condition", _condition);

    if (_metadata.Count == 0)
    {
      sb.AppendLine(" />");
    }
    else
    {
      sb.AppendLine(">");
      foreach (var metadata in _metadata)
      {
        metadata.AppendXml(sb, indentLevel + 1);
      }
      AppendIndent(sb, indentLevel);
      sb.AppendLine($"</{_itemType}>");
    }
  }
}