using System;
using EasyDotnet.MTP.RPC.Models;
using EasyDotnet.Types;
using StreamJsonRpc;

namespace EasyDotnet.Extensions;

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

  public static TestRunResult ToTestRunResult(this TestNodeUpdate test) => new()
  {
    Id = test.Node.Uid,
    Outcome = test.Node.ExecutionState,
    Duration = (long?)test.Node.Duration,
    ErrorMessage = test.Node.Message?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? [],
    StackTrace = (test.Node.StackTrace?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? []).AsAsyncEnumerable(),
    StdOut = (test.Node.StandardOutput?.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries) ?? []).AsAsyncEnumerable()
  };
}