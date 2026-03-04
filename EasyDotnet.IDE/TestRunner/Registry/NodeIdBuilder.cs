namespace EasyDotnet.IDE.TestRunner.Registry;

/// <summary>
/// Generates stable, deterministic node IDs that encode enough structural
/// information to survive rediscovery. Native framework IDs are never used here.
/// 
/// Format: each segment separated by "::" to avoid clashing with dots in namespaces.
/// 
/// solution    → "MySolution.sln"
/// project     → "MySolution.sln::MyProject::net8.0"
/// namespace   → "MySolution.sln::MyProject::net8.0::My.App.Tests"
/// class       → "MySolution.sln::MyProject::net8.0::My.App.Tests::MyClass"
/// method      → "MySolution.sln::MyProject::net8.0::My.App.Tests::MyClass::MyMethod"
/// subcase     → "MySolution.sln::MyProject::net8.0::My.App.Tests::MyClass::MyMethod(1, 2)"
/// </summary>
public static class NodeIdBuilder
{
    public static string Solution(string solutionName) =>
        solutionName;

    public static string Project(string solutionId, string projectName, string tfm) =>
        $"{solutionId}::{projectName}::{tfm}";

    public static string Namespace(string projectNodeId, IReadOnlyList<string> namespaceParts) =>
        $"{projectNodeId}::{string.Join(".", namespaceParts)}";

    // Overload for adding one more segment onto an existing namespace ID
    public static string Namespace(string parentNamespaceId, string segment) =>
        $"{parentNamespaceId}.{segment}";

    public static string Class(string namespaceNodeId, string className) =>
        $"{namespaceNodeId}::{className}";

    public static string Method(string classNodeId, string methodName) =>
        $"{classNodeId}::{methodName}";

    // For Expecto-style tests with no class — method hangs directly off namespace
    public static string MethodNoClass(string namespaceNodeId, string methodName) =>
        $"{namespaceNodeId}::{methodName}";
}
