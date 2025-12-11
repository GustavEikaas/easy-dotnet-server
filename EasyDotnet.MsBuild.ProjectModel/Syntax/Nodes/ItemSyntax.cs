using System.Collections.Immutable;

namespace EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

public sealed record ItemSyntax : MsBuildSyntaxNode
{
  public required string ItemType { get; init; }
  public required string Include { get; init; }
  public string? Exclude { get; init; }
  public string? Remove { get; init; }
  public string? Update { get; init; }
  public string? Condition { get; init; }
  public required ImmutableArray<MetadataSyntax> Metadata { get; init; }

  public string? GetMetadataValue(string name) => Metadata.FirstOrDefault(m => m.Name == name)?.Value;

  public override ImmutableArray<MsBuildSyntaxNode> Children
  {
    get => [.. Metadata.Cast<MsBuildSyntaxNode>()];
    init => base.Children = value;
  }
}