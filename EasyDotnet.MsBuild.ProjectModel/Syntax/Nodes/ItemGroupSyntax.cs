using System.Collections.Immutable;

namespace EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

public sealed record ItemGroupSyntax : MsBuildSyntaxNode
{
  public required string? Condition { get; init; }
  public required ImmutableArray<ItemSyntax> Items { get; init; }

  public override ImmutableArray<MsBuildSyntaxNode> Children
  {
    get => [.. Items.Cast<MsBuildSyntaxNode>()];
    init => base.Children = value;
  }
}