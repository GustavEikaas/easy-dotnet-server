using System.Collections.Immutable;

namespace EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;


public sealed record PropertyGroupSyntax : MsBuildSyntaxNode
{
  public required string? Condition { get; init; }
  public required ImmutableArray<PropertySyntax> Properties { get; init; }

  public override ImmutableArray<MsBuildSyntaxNode> Children
  {
    get => [.. Properties.Cast<MsBuildSyntaxNode>()];
    init => base.Children = value;
  }
}