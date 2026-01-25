using EasyDotnet.Domain.Models.Test;
using Newtonsoft.Json;

namespace EasyDotnet.Domain.Models.MTP;

public sealed record TestNodeUpdate
(
  [property: JsonProperty("node")]
  TestNode Node,

  [property: JsonProperty("parent")]
  string ParentUid);

public sealed record TestNode
(
  [property: JsonProperty("uid")]
  string Uid,

  [property: JsonProperty("display-name")]
  string DisplayName,

  [property: JsonProperty("location.namespace")]
  string? TestNamespace,

  [property: JsonProperty("location.method")]
  string TestMethod,

  [property: JsonProperty("location.type")]
  string TestType,

  [property: JsonProperty("location.file")]
  string? FilePath,

  [property: JsonProperty("location.line-start")]
  int? LineStart,

  [property: JsonProperty("location.line-end")]
  int? LineEnd,

  [property: JsonProperty("error.message")]
  string? Message,

  [property: JsonProperty("error.stacktrace")]
  string? StackTrace,

  [property: JsonProperty("time.duration-ms")]
  float? Duration,

  [property: JsonProperty("node-type")]
  string NodeType,

  [property: JsonProperty("execution-state")]
  string ExecutionState,

  [property: JsonProperty("standardOutput")]
  string? StandardOutput
);


public static class TestNodeExtensions
{
  public static DiscoveredTest ToDiscoveredTest(this TestNodeUpdate update)
  {
    var node = update.Node;

    var (fqn, rawLeafName) = ResolveNaming(node);

    var parts = fqn.Split('.', StringSplitOptions.RemoveEmptyEntries);
    var namespacePath = parts.Length > 0 ? parts[..^1] : [];

    var (cleanName, args) = ParseArguments(rawLeafName);

    return new DiscoveredTest
    {
      Id = node.Uid,
      FullyQualifiedName = fqn,
      NamespacePath = namespacePath,
      DisplayName = cleanName,
      Arguments = args,
      FilePath = node.FilePath?.Replace("\\", "/"),
      LineNumber = node.LineStart.HasValue ? node.LineStart.Value - 1 : null
    };
  }

  /// <summary>
  /// Resolves the Fully Qualified Name (FQN) and the Display Name (Leaf) based on the available metadata.
  /// <para>
  /// <b>Scenario 1: Standard MTP (TUnit, NUnit)</b>
  /// <br/>Type: <c>MyNs.MyClass</c>, Display: <c>MyMethod</c>
  /// <br/>Result FQN: <c>MyNs.MyClass.MyMethod</c>
  /// <br/>Result Leaf: <c>MyMethod</c>
  /// </para>
  /// <para>
  /// <b>Scenario 2: Expecto / F# / Scripts</b>
  /// <br/>Type: <c>null</c>, Display: <c>samples.universe exists</c>
  /// <br/>Result FQN: <c>samples.universe exists</c>
  /// <br/>Result Leaf: <c>universe exists</c> (Last segment)
  /// </para>
  /// <para>
  /// <b>Scenario 3: Root Level Test</b>
  /// <br/>Type: <c>null</c>, Display: <c>GlobalTest</c>
  /// <br/>Result FQN: <c>GlobalTest</c>
  /// <br/>Result Leaf: <c>GlobalTest</c>
  /// </para>
  /// </summary>
  private static (string Fqn, string RawLeafName) ResolveNaming(TestNode node)
  {
    if (!string.IsNullOrEmpty(node.TestType))
    {
      return ($"{node.TestType}.{node.DisplayName}", node.DisplayName);
    }

    var fqn = node.DisplayName;
    var lastDot = fqn.LastIndexOf('.');

    var rawLeafName = lastDot >= 0 ? fqn[(lastDot + 1)..] : fqn;

    return (fqn, rawLeafName);
  }

  private static (string Name, string? Args) ParseArguments(string rawName)
  {
    var start = rawName.IndexOf('(');
    var end = rawName.LastIndexOf(')');

    if (start >= 0 && end > start && end == rawName.Length - 1)
    {
      var args = rawName[start..(end + 1)];

      return (rawName, args);
    }

    return (rawName, null);
  }
}