using EasyDotnet.IDE.TestRunner.Adapters.MTP.RPC.Models;
using EasyDotnet.IDE.TestRunner.Models;

namespace EasyDotnet.IDE.TestRunner.Adapters;

public static class MtpExtensions
{
  public static DiscoveredTest ToDiscoveredTest(this TestNodeUpdate update)
  {
    var node = update.Node;

    string? className;
    IReadOnlyList<string> namespaceParts;
    string rawLeaf;

    if (!string.IsNullOrEmpty(node.TestType))
    {
      // Standard MTP (TUnit, NUnit, MSTest v3)
      // TestType = "My.App.Tests.MyClass" → ns=["My","App","Tests"], class="MyClass"
      var typeParts = node.TestType.Split('.');
      className = typeParts[^1];
      namespaceParts = typeParts.Length > 1 ? typeParts[..^1] : [];
      rawLeaf = node.DisplayName;
    }
    else
    {
      // Expecto / F# / scripts — no type information
      // DisplayName = "samples.universe exists" → treat all but last segment as ns
      var lastDot = node.DisplayName.LastIndexOf('.');
      className = null;
      namespaceParts = lastDot >= 0
          ? node.DisplayName[..lastDot].Split('.')
          : [];
      rawLeaf = lastDot >= 0 ? node.DisplayName[(lastDot + 1)..] : node.DisplayName;
    }

    var (methodName, args) = ParseArguments(rawLeaf);
    var fqn = !string.IsNullOrEmpty(node.TestType)
        ? $"{node.TestType}.{node.DisplayName}"
        : node.DisplayName;

    return new DiscoveredTest
    {
      NativeId = node.Uid,
      FullyQualifiedName = fqn,
      NamespaceParts = namespaceParts,
      ClassName = className,
      MethodName = methodName,
      DisplayName = methodName,
      Arguments = args,
      FilePath = node.FilePath?.Replace("\\", "/"),
      // MTP is 1-based → convert to 0-based (LSP standard)
      LineNumber = node.LineStart.HasValue ? node.LineStart.Value - 1 : null,
    };
  }

  public static TestRunResult? ToTestRunResult(this TestNodeUpdate update)
  {
    var node = update.Node;

    // Only terminal states produce a result
    if (node.ExecutionState is "discovered" or "in-progress") return null;

    var frames = SimpleStackTraceParser.Parse(node.StackTrace);
    var failingFrame = frames.FirstOrDefault(f => f.IsUserCode);
    var stdout = node.StandardOutput
        ?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? [];

    return new TestRunResult
    {
      NativeId = node.Uid,
      Outcome = MapOutcome(node.ExecutionState),
      DurationMs = node.Duration.HasValue ? (long)node.Duration.Value : null,
      ErrorMessage = node.Message
            ?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? [],
      Stdout = stdout,
      Frames = frames,
      FailingFrame = failingFrame,
    };
  }

  private static string MapOutcome(string executionState) =>
      executionState switch
      {
        "passed" => "passed",
        "failed" => "failed",
        "error" => "failed",
        "skipped" => "skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(executionState), executionState, "Unmapped MTP execution state")
      };

  private static (string MethodName, string? Args) ParseArguments(string rawName)
  {
    var start = rawName.IndexOf('(');
    var end = rawName.LastIndexOf(')');
    if (start >= 0 && end > start && end == rawName.Length - 1)
      return (rawName[..start], rawName[start..(end + 1)]);
    return (rawName, null);
  }
}