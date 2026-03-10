namespace EasyDotnet.IDE.TestRunner.Models;

public abstract record NodeType
{
  public string Type => GetType().Name;

  public sealed record Solution : NodeType;
  public sealed record Project : NodeType;
  public sealed record Namespace : NodeType;
  public sealed record TestClass : NodeType;
  public sealed record TheoryGroup : NodeType;
  public sealed record TestMethod : NodeType;
  public sealed record Subcase : NodeType;
}