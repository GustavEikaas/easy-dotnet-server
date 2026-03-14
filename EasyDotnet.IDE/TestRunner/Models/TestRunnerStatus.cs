using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EasyDotnet.IDE.TestRunner.Models;

/// <summary>
/// Broadcast whenever aggregate state changes.
/// Lua uses IsLoading to lock/unlock the action keymaps.
/// </summary>
public record TestRunnerStatus(
    bool IsLoading,
    string? CurrentOperation,
    OverallStatus OverallStatus,
    int TotalTests,
    int TotalRunning,
    int TotalPassed,
    int TotalFailed,
    int TotalSkipped,
    int TotalCancelled
);

[JsonConverter(typeof(StringEnumConverter))]
public enum OverallStatus
{
  Idle,
  Building,
  Discovering,
  Running,
  Debugging,
  Cancelling,
  Cancelled,
  Killing,
  Killed,
  Failed,
  Passed
}