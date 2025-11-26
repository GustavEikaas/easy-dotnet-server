using Newtonsoft.Json;

namespace EasyDotnet.TestRunner.Models;

public record TestNode(
    string Id,
    string DisplayName,
    string? ParentId,
    string? FilePath,
    int? LineNumber,
    NodeType Type);

[JsonConverter(typeof(UnionTypeNameConverter<NodeType>))]
public abstract record NodeType
{
  public sealed record Solution : NodeType;
  public sealed record Project : NodeType;
  public sealed record Namespace : NodeType;
  // TODO: can we add TestClass?
  // public sealed record TestClass : NodeType;
  public sealed record TestMethod : NodeType;
  public sealed record Subcase : NodeType;
}

public record ProjectTfm(
    string Id,
    string ProjectFilePath,
    string DisplayName,
    string TargetFramework
);

public static class ProjectTfmExtensions
{
  public static TestNode ToTestNode(this ProjectTfm tfm, string solutionNodeId) => new(
        Id: tfm.Id,
        DisplayName: $"{tfm.DisplayName} ({tfm.TargetFramework})",
        ParentId: solutionNodeId,
        FilePath: tfm.ProjectFilePath,
        LineNumber: null,
        Type: new NodeType.Project()
    );
}