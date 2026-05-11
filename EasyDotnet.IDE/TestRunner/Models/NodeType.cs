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

  /// <summary>
  /// Method in source that carries a known test-attribute marker but isn't (yet)
  /// present in the compiled assembly — e.g. a test the user just wrote. Emitted
  /// by <c>testrunner/syncFile</c> so the client can render provisional signs
  /// before the next build/discover pass, and reconciled to a real TestMethod
  /// on the next discovery.
  /// </summary>
  public sealed record ProbableTest : NodeType;
}