using EasyDotnet.IDE.TestRunner.Models;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace EasyDotnet.IDE.TestRunner.Adapters;

public static class VsTestExtensions
{
  public static DiscoveredTest ToDiscoveredTest(this TestCase x)
  {
    // TestCaseProperties does not expose ManagedType in this ObjectModel version.
    // We derive namespace + class from FullyQualifiedName for all VSTest frameworks.
    //
    // FQN formats by framework:
    //   MSTest:  "My.App.Tests.MyClass.TestMethod1"
    //   NUnit:   "My.App.Tests.MyClass.TestMethod1"
    //   xUnit 2: "My.App.Tests.MyClass.TestMethod1"
    //            (display name may be "TestMethod1" or full FQN depending on version)
    //   Expecto: "samples.universe exists (╭ರᴥ•́)"  — no class, dot is part of test name
    //
    // Strategy: strip the argument suffix first, then split on dots.
    // Second-to-last segment = class, everything before = namespace.
    // For Expecto-style (display name contains spaces), FQN splitting still gives a
    // reasonable namespace grouping even if the class segment is synthetic.

    var fqnWithoutArgs = StripArguments(x.FullyQualifiedName);
    var fqnParts = fqnWithoutArgs.Split('.', StringSplitOptions.RemoveEmptyEntries);

    string? className;
    IReadOnlyList<string> namespaceParts;

    if (fqnParts.Length >= 2)
    {
      // Last segment is method name in FQN; second-to-last is class
      className = fqnParts[^2];
      namespaceParts = fqnParts.Length > 2 ? fqnParts[..^2] : [];
    }
    else
    {
      className = null;
      namespaceParts = [];
    }

    var (methodName, args) = ParseArguments(x.DisplayName);

    return new DiscoveredTest
    {
      NativeId = x.Id.ToString(),
      FullyQualifiedName = x.FullyQualifiedName,
      NamespaceParts = namespaceParts,
      ClassName = className,
      MethodName = methodName,
      DisplayName = x.DisplayName,
      Arguments = args,
      FilePath = x.CodeFilePath?.Replace("\\", "/"),
      // VSTest LineNumber is already 0-based for MSTest; others may vary — treat negative as null
      LineNumber = x.LineNumber >= 0 ? x.LineNumber : null,
    };
  }

  public static TestRunResult ToTestRunResult(this TestResult x)
  {
    var parsedFrames = SimpleStackTraceParser.Parse(x.ErrorStackTrace);
    var failingFrame = parsedFrames.FirstOrDefault(f => f.IsUserCode);
    var stdout = GetStandardOutput(x)
        ?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? [];

    return new TestRunResult
    {
      NativeId = x.TestCase.Id.ToString(),
      Outcome = GetOutcome(x.Outcome),
      DurationMs = (long?)x.Duration.TotalMilliseconds,
      ErrorMessage = x.ErrorMessage
            ?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? [],
      Stdout = stdout,
      Frames = parsedFrames,
      FailingFrame = failingFrame,
    };
  }

  private static string GetOutcome(TestOutcome outcome) =>
      outcome switch
      {
        TestOutcome.Passed => "passed",
        TestOutcome.Failed => "failed",
        TestOutcome.Skipped => "skipped",
        _ => "none"
      };

  private static string? GetStandardOutput(TestResult result) =>
      result.Messages
          .FirstOrDefault(m => m.Category == TestResultMessage.StandardOutCategory)
          ?.Text;

  private static (string MethodName, string? Args) ParseArguments(string displayName)
  {
    var start = displayName.IndexOf('(');
    var end = displayName.LastIndexOf(')');
    if (start >= 0 && end > start && end == displayName.Length - 1)
      return (displayName, displayName[start..(end + 1)]);
    return (displayName, null);
  }

  /// <summary>
  /// Strips the argument suffix from a FQN before splitting on dots,
  /// so that "My.Tests.MyClass.Method(a, b.c)" doesn't produce extra segments.
  /// </summary>
  private static string StripArguments(string fqn)
  {
    var parenIdx = fqn.IndexOf('(');
    return parenIdx >= 0 ? fqn[..parenIdx] : fqn;
  }
}