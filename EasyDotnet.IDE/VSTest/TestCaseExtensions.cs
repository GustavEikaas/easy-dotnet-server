using EasyDotnet.IDE;
using EasyDotnet.Types;

using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace EasyDotnet.VSTest;

public static class TestCaseExtensions
{
  public static DiscoveredTest ToDiscoveredTest(this TestCase x)
  {
    var name = x.DisplayName.Contains('.') ? x.DisplayName : x.FullyQualifiedName;
    return new()
    {
      Id = x.Id.ToString(),
      Namespace = x.FullyQualifiedName,
      Name = name,
      FilePath = x.CodeFilePath?.Replace("\\", "/"),
      LineNumber = x.LineNumber,
      DisplayName = x.DisplayName
    };
  }

  public static TestRunResult ToTestRunResult(this TestResult x)
  {
    var parsedStack = SimpleStackTraceParser.Parse(x.ErrorStackTrace);
    var errorLocation = parsedStack.FirstOrDefault(frame => frame.IsUserCode);
    return new()
    {
      Duration = (long?)x.Duration.TotalMilliseconds,
      StackTrace = (x.ErrorStackTrace?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? []).ToBatchedAsyncEnumerable(30),
      PrettyStackTrace = parsedStack.ToBatchedAsyncEnumerable(10),
      FailingFrame = errorLocation,
      ErrorMessage = x.ErrorMessage?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? [],
      Id = x.TestCase.Id.ToString(),
      Outcome = GetTestOutcome(x.Outcome),
      StdOut = (x.GetStandardOutput()?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? []).ToBatchedAsyncEnumerable(30),
    };
  }

  public static string GetTestOutcome(TestOutcome outcome) => outcome switch
  {
    TestOutcome.None => "none",
    TestOutcome.Passed => "passed",
    TestOutcome.Failed => "failed",
    TestOutcome.Skipped => "skipped",
    TestOutcome.NotFound => "not found",
    _ => "",
  };

  private static string? GetStandardOutput(this TestResult testResult)
    => testResult.Messages.FirstOrDefault(message => message.Category == TestResultMessage.StandardOutCategory)?.Text;
}