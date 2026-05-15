namespace EasyDotnet.IDE.TestRunner.Registry;

/// <summary>
/// Generates stable, deterministic node IDs that encode enough structural
/// information to survive rediscovery. Native framework IDs are never used here.
/// 
/// Format: each segment separated by "::" to avoid clashing with dots in namespaces.
/// 
/// solution    → "MySolution.sln"
/// project     → "MySolution.sln::MyProject::net8.0"
/// namespace   → "MySolution.sln::MyProject::net8.0::ns:My.App.Tests"
/// class       → "MySolution.sln::MyProject::net8.0::ns:My.App.Tests::class:MyClass"
/// method      → "MySolution.sln::MyProject::net8.0::ns:My.App.Tests::class:MyClass::method:MyMethod"
/// subcase     → "MySolution.sln::MyProject::net8.0::ns:My.App.Tests::class:MyClass::method:MyMethod(1, 2)"
/// </summary>
public static class NodeIdBuilder
{
  public static string Solution(string solutionName) =>
      solutionName;

  public static string Project(string solutionId, string projectName, string tfm) =>
      $"{solutionId}::{projectName}::{tfm}";

  public static string Namespace(string projectNodeId, IReadOnlyList<string> namespaceParts) =>
      $"{projectNodeId}::ns:{string.Join(".", namespaceParts)}";

  public static string Namespace(string parentNamespaceId, string segment) =>
      $"{parentNamespaceId}.ns:{segment}";

  public static string Class(string namespaceNodeId, string className) =>
      $"{namespaceNodeId}::class:{className}";

  public static string TheoryGroup(string classNodeId, string methodName) =>
      $"{classNodeId}::theory:{methodName}";

  public static string Method(string classNodeId, string methodName) =>
      $"{classNodeId}::method:{methodName}";

  public static string MethodNoClass(string namespaceNodeId, string methodName) =>
      $"{namespaceNodeId}::method:{methodName}";
}