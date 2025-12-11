using System.Collections.Immutable;

namespace EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;


public sealed record ProjectSyntax : MsBuildSyntaxNode
{
  public required string? Sdk { get; init; }
  public required ImmutableArray<PropertyGroupSyntax> PropertyGroups { get; init; }
  public required ImmutableArray<ItemGroupSyntax> ItemGroups { get; init; }
  public required ImmutableArray<TargetSyntax> Targets { get; init; }
  public required ImmutableArray<ImportSyntax> Imports { get; init; }

  public override ImmutableArray<MsBuildSyntaxNode> Children
  {
    get => [.. PropertyGroups, .. ItemGroups, .. Targets, .. Imports];
    init => base.Children = value;
  }
}