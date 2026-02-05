using System;
using EasyDotnet.Domain.Models.MTP;
using EasyDotnet.Domain.Models.Test;

namespace EasyDotnet.Extensions;

public static class TestNodeExtensions
{
  public static TestRunResult ToTestRunResult(this TestNodeUpdate test) => new()
  {
    Id = test.Node.Uid,
    Outcome = test.Node.ExecutionState,
    Duration = (long?)test.Node.Duration,
    ErrorMessage = test.Node.Message?.Split(Environment.NewLine) ?? [],
    StackTrace = (test.Node.StackTrace?.Split(Environment.NewLine) ?? []),
    StdOut = (test.Node.StandardOutput?.Split(Environment.NewLine) ?? []),
  };
}