namespace EasyDotnet.MsBuild.ProjectModel.Syntax.Nodes;

public sealed record TargetSyntax : MsBuildSyntaxNode
{
  public required string Name { get; init; }
  public required string? BeforeTargets { get; init; }
  public required string? AfterTargets { get; init; }
  public required string? Condition { get; init; }
}