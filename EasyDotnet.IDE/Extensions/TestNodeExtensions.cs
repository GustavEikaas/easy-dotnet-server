using EasyDotnet.MTP.RPC.Models;
using EasyDotnet.Types;

namespace EasyDotnet.IDE.Extensions;

public static class TestNodeExtensions
{
  public static DiscoveredTest ToDiscoveredTest(this TestNodeUpdate test)
  {
    //TODO: I need to document this somewhere
    var name = string.IsNullOrEmpty(test.Node.TestNamespace) ? $"{test.Node.TestType}.{test.Node.DisplayName}" : $"{test.Node.TestNamespace}.{test.Node.DisplayName}";
    return new()
    {
      Id = test.Node.Uid,
      FilePath = test.Node?.FilePath?.Replace("\\", "/"),
      LineNumber = test.Node?.LineStart,
      Namespace = test.Node?.TestNamespace,
      DisplayName = test.Node?.DisplayName ?? "Unknown",
      Name = name
    };
  }

  public static TestRunResult ToTestRunResult(this TestNodeUpdate test)
  {
    var parsedStack = SimpleStackTraceParser.Parse(test.Node.StackTrace);
    var errorLocation = parsedStack.FirstOrDefault(frame => frame.IsUserCode);
    return new()
    {
      Id = test.Node.Uid,
      Outcome = test.Node.ExecutionState,
      Duration = (long?)test.Node.Duration,
      ErrorMessage = test.Node.Message?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? [],
      PrettyStackTrace = parsedStack.ToBatchedAsyncEnumerable(10),
      FailingFrame = errorLocation,
      StackTrace = (test.Node.StackTrace?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? []).ToBatchedAsyncEnumerable(30),
      StdOut = (test.Node.StandardOutput?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? []).ToBatchedAsyncEnumerable(30)
    };
  }
}