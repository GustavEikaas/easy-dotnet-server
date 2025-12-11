namespace EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

public sealed record MetadataSyntax : MsBuildSyntaxNode
{
  public required string Name { get; init; }
  public required string Value { get; init; }
}