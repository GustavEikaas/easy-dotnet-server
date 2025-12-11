namespace EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

public sealed record ImportSyntax : MsBuildSyntaxNode
{
  public required string Project { get; init; }
  public required string? Sdk { get; init; }
  public required string? Condition { get; init; }
}